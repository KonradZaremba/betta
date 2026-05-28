// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Betta.Services
{
    /// <summary>
    /// Describes a component that should be generated from a service method
    /// </summary>
    public class ComponentDescriptor
    {
        public Type ServiceType { get; set; }
        public MethodInfo Method { get; set; }
        public string Name { get; set; }
        public string NickName { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string SubCategory { get; set; }
        public Guid Guid { get; set; }
        public bool Enabled { get; set; } = true;
        public string IconResource { get; set; }
        internal System.Drawing.Bitmap CachedIcon { get; set; }

        /// <summary>
        /// Deterministic GUID from service type + method signature. Stable across
        /// .NET Framework and .NET 7; unlike string.GetHashCode() which is not.
        /// Signature (not cosmetic attributes) is hashed so renaming Name/Category
        /// does not invalidate existing .gh documents.
        /// </summary>`
        public static Guid GenerateGuidFromMethod(Type serviceType, MethodInfo method)
        {
            var paramSig = string.Join(",",
                method.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name));
            var input = $"{serviceType.FullName}|{method.Name}|{paramSig}|{method.ReturnType.FullName}";

            using var md5 = MD5.Create();
            var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            return new Guid(bytes);
        }

        public override string ToString()
        {
            return $"{ServiceType.Name}.{Method.Name} -> {Name}";
        }
    }
}
