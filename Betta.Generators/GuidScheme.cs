// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Compiled into Betta.Generators and, as a linked file, into Betta and Betta.Mcp, so the generator,
// the runtime and the MCP server derive component GUIDs the same way. No Roslyn types here.
#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Betta.Guids
{
    /// <summary>
    /// The component GUID formula. Any change here breaks saved .gh files (see TestGuidScheme).
    /// The string layer is used by all three projects; the reflection layer by Betta and Betta.Mcp
    /// (MetadataLoadContext types are System.Type too).
    /// </summary>
    internal static class GuidScheme
    {
        // ---- String layer -------------------------------------------------

        public static Guid Md5(string input)
        {
            using (var md5 = MD5.Create())
                return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(input)));
        }

        /// <summary>Type|Method: the GUID input of every component that isn't an overload.</summary>
        public static string NameInput(string typeName, string methodName) => typeName + "|" + methodName;

        /// <summary>Type|Method|paramType name,...|ReturnType: an overload's input; with Type.FullName names, the pre-0.8 one.</summary>
        public static string SignatureInput(string typeName, string methodName, IEnumerable<string> typedParameters, string returnTypeName) =>
            typeName + "|" + methodName + "|" + string.Join(",", typedParameters) + "|" + returnTypeName;

        /// <summary>
        /// The inputs of the GUIDs a method had under its FormerNames: earlier method names, "Ns.Type.Method"
        /// for a moved method, earlier type names. An overload listing its own name keeps its name-only input.
        /// </summary>
        public static IEnumerable<string> FormerInputs(string typeName, string methodName, bool overloaded,
            IEnumerable<string> formerMethodNames, IEnumerable<string> formerTypeNames)
        {
            var methodNames = Clean(formerMethodNames).ToList();
            var names = new[] { methodName }.Concat(methodNames.Where(n => n.IndexOf('.') < 0)).ToList();
            var current = NameInput(typeName, methodName);
            var keepCurrent = overloaded && methodNames.Contains(methodName);
            return new[] { typeName }.Concat(Clean(formerTypeNames))
                .SelectMany(t => names.Select(m => NameInput(t, m)))
                // A dotted former name is "Type.Method": its last dot becomes the hash's separator.
                .Concat(methodNames.Where(n => n.IndexOf('.') >= 0)
                    .Select(n => { var dot = n.LastIndexOf('.'); return NameInput(n.Substring(0, dot), n.Substring(dot + 1)); }))
                .Where(i => keepCurrent || i != current)
                .Distinct()
                .ToList();
        }

        private static IEnumerable<string> Clean(IEnumerable<string> names) =>
            (names ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim());

        // ---- Reflection layer ---------------------------------------------

        /// <summary>
        /// <c>Type.FullName</c> without assembly qualification: a closed generic is written
        /// <c>List`1[System.Double]</c> instead of <c>List`1[[System.Double, System.Private.CoreLib, Version=…]]</c>.
        /// Equal to FullName for every type without generic arguments.
        /// </summary>
        public static string StableTypeName(Type type)
        {
            if (type.IsArray)
            {
                var rank = type.GetArrayRank();
                return StableTypeName(type.GetElementType()) + (rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]");
            }
            if (type.IsByRef) return StableTypeName(type.GetElementType()) + "&";
            if (type.IsPointer) return StableTypeName(type.GetElementType()) + "*";
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
                return type.GetGenericTypeDefinition().FullName + "[" +
                       string.Join(",", type.GetGenericArguments().Select(StableTypeName)) + "]";
            return type.FullName ?? type.Name;
        }

        /// <summary>
        /// True when the service type has another public component method with the same name
        /// (disabled ones count, so toggling Enabled changes no GUID).
        /// </summary>
        public static bool IsOverloaded(Type serviceType, MethodInfo method, Func<MethodInfo, bool> isComponent) =>
            serviceType.GetMethods().Count(m => m.Name == method.Name && isComponent(m)) > 1;

        /// <summary>The published GUID: the signature hash for an overload, else Type|Method.</summary>
        public static Guid MethodGuid(Type serviceType, MethodInfo method, bool overloaded) =>
            overloaded
                ? SignatureGuid(serviceType, method, StableTypeName)
                : Md5(NameInput(StableTypeName(serviceType), method.Name));

        /// <summary>The pre-0.8 GUID: the signature hash with Type.FullName names.</summary>
        public static Guid LegacyGuid(Type serviceType, MethodInfo method) =>
            SignatureGuid(serviceType, method, t => t.FullName);

        private static Guid SignatureGuid(Type serviceType, MethodInfo method, Func<Type, string> typeName) =>
            // The service type is written with FullName here, not StableTypeName: kept as it always was.
            Md5(SignatureInput(serviceType.FullName, method.Name,
                method.GetParameters().Select(p => typeName(p.ParameterType) + " " + p.Name),
                typeName(method.ReturnType)));
    }
}
