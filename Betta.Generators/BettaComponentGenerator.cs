// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Betta.Generators
{
    /// <summary>
    /// Real Betta source generator. Walks the compilation for types that
    /// implement <c>Betta.Interfaces.IBettaCollection</c> (either an interface
    /// inheriting it or a concrete class) and carry <c>[GrasshopperMethod]</c>
    /// on their members. For each such method it emits a
    /// <c>ComponentDescriptorBlueprint</c> literal — including a compile-time
    /// MD5-derived GUID — into a static partial class
    /// <c>Betta.Generated.BettaPluginBootstrap</c>. At startup Betta sees the
    /// generated bootstrap, converts blueprints to <c>ComponentDescriptor</c>s
    /// and skips the reflection scan for that assembly. Both paths produce
    /// identical descriptors so save/reload compatibility is preserved.
    ///
    /// Notes on the type-name matching strategy:
    ///   - The generator stays free of a hard reference to Betta.Abstractions:
    ///     attributes are matched by their simple name (e.g. "GrasshopperMethod"
    ///     / "GrasshopperMethodAttribute"). That keeps the analyzer load self
    ///     contained even when Abstractions ships separately.
    ///   - The marker interface is matched by full name
    ///     ("Betta.Interfaces.IBettaCollection").
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public class BettaComponentGenerator : IIncrementalGenerator
    {
        private const string IBettaCollectionFullName = "Betta.Interfaces.IBettaCollection";

        private const string GrasshopperMethodAttr = "GrasshopperMethod";
        private const string GrasshopperParameterAttr = "GrasshopperParameter";
        private const string GrasshopperCollectionAttr = "GrasshopperCollection";
        private const string GrasshopperOpaqueAttr = "GrasshopperOpaque";

        private const string PreviewInterfaceFullName = "Betta.Preview.IBettaPreview";
        private const string BakeableInterfaceFullName = "Betta.Preview.IBettaBakeable";
        private const string BettaValueInterfaceFullName = "Betta.Interfaces.IBettaValue";

        // Diagnostics
        private static readonly DiagnosticDescriptor BETTA001_UnsupportedParameterType = new(
            id: "BETTA001",
            title: "Parameter type not in Betta-supported set",
            messageFormat: "Parameter '{0}' on '{1}.{2}' has type '{3}' which is not in the Betta-supported set (primitives, Rhino.Geometry.*, opaque-marker types, List<T>, Tuple, Task<T>). The component may fail at runtime.",
            category: "Betta",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor BETTA010_NameAttributeOverridesClrName = new(
            id: "BETTA010",
            title: "GrasshopperParameter.Name differs from the CLR parameter name",
            messageFormat: "Parameter '{0}' on '{1}.{2}' has [GrasshopperParameter(Name = \"{3}\")] but the runtime hashes the CLR parameter name into the component GUID — the attribute Name affects the display only.",
            category: "Betta",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Pick up every type declaration in the compilation; filter inside
            // the transform stage when we have a SemanticModel.
            var collectionTypes = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax t && t.AttributeLists.Count + (t.BaseList?.Types.Count ?? 0) > 0,
                transform: static (ctx, ct) => Extract(ctx, ct))
                .Where(static x => x is not null);

            var collected = collectionTypes.Collect();
            var compilationAndCollected = context.CompilationProvider.Combine(collected);

            context.RegisterSourceOutput(compilationAndCollected, static (spc, tuple) =>
            {
                var (_, items) = tuple;
                // Distinct by serviceTypeFullName to merge partial-class halves.
                var byType = new Dictionary<string, ServiceTypeModel>(StringComparer.Ordinal);
                foreach (var item in items)
                {
                    if (item == null) continue;
                    if (!byType.ContainsKey(item.FullName))
                        byType[item.FullName] = item;
                    else
                    {
                        // Merge methods from a partial-class half.
                        var existing = byType[item.FullName];
                        foreach (var m in item.Methods) existing.Methods.Add(m);
                    }
                }

                foreach (var diag in items.SelectMany(i => i?.Diagnostics ?? Enumerable.Empty<Diagnostic>()))
                    spc.ReportDiagnostic(diag);

                if (byType.Count == 0) return;

                var src = EmitBootstrap(byType.Values);
                spc.AddSource("BettaPluginBootstrap.g.cs", src);
            });
        }

        // ---- Syntax → semantic model extraction --------------------------

        private static ServiceTypeModel Extract(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
        {
            if (ctx.Node is not TypeDeclarationSyntax tds) return null;
            if (ctx.SemanticModel.GetDeclaredSymbol(tds, ct) is not INamedTypeSymbol typeSym) return null;

            if (!ImplementsIBettaCollection(typeSym)) return null;

            // For each method that carries [GrasshopperMethod], build a model.
            var methods = new List<MethodModel>();
            var diagnostics = new List<Diagnostic>();

            var collectionAttr = FindAttribute(typeSym.GetAttributes(), GrasshopperCollectionAttr);
            var (defaultCategory, defaultSubCategory) = ExtractCollectionDefaults(collectionAttr, typeSym);

            foreach (var member in typeSym.GetMembers())
            {
                if (member is not IMethodSymbol m) continue;
                if (m.MethodKind != MethodKind.Ordinary) continue;
                if (m.DeclaredAccessibility != Accessibility.Public) continue;

                var ghAttr = FindAttribute(m.GetAttributes(), GrasshopperMethodAttr);
                if (ghAttr == null) continue;

                var enabled = NamedArgBool(ghAttr, "Enabled", true);
                // Constructor positional name (string name, string description = null)
                string ctorName = null, ctorDesc = null;
                if (ghAttr.ConstructorArguments.Length >= 1) ctorName = ghAttr.ConstructorArguments[0].Value as string;
                if (ghAttr.ConstructorArguments.Length >= 2) ctorDesc = ghAttr.ConstructorArguments[1].Value as string;

                var name = NamedArgString(ghAttr, "Name", ctorName) ?? m.Name;
                var nickName = NamedArgString(ghAttr, "NickName", null) ?? name;
                var desc = NamedArgString(ghAttr, "Description", ctorDesc) ?? name;
                var category = NamedArgString(ghAttr, "Category", null) ?? defaultCategory;
                var subCategory = NamedArgString(ghAttr, "SubCategory", null) ?? defaultSubCategory;
                var iconResource = NamedArgString(ghAttr, "IconResource", null);
                var explicitGuid = NamedArgString(ghAttr, "Guid", null);

                var serviceFullName = ReflectionFullName(typeSym);
                var parameters = m.Parameters
                    .Select(p => new ParamModel(p.Name, ReflectionFullName(p.Type)))
                    .ToArray();
                var returnFullName = ReflectionFullName(m.ReturnType);

                // Diagnostics
                foreach (var p in m.Parameters)
                {
                    if (!IsSupportedParameterType(p.Type))
                    {
                        diagnostics.Add(Diagnostic.Create(
                            BETTA001_UnsupportedParameterType,
                            p.Locations.FirstOrDefault() ?? m.Locations.FirstOrDefault() ?? Location.None,
                            p.Name, typeSym.Name, m.Name, p.Type.ToDisplayString()));
                    }

                    var paramAttr = FindAttribute(p.GetAttributes(), GrasshopperParameterAttr);
                    if (paramAttr != null)
                    {
                        // ctor: (string name, string description = null) OR (name, nickName, description)
                        string ctorParamName = null;
                        if (paramAttr.ConstructorArguments.Length >= 1)
                            ctorParamName = paramAttr.ConstructorArguments[0].Value as string;
                        var attrName = NamedArgString(paramAttr, "Name", ctorParamName);
                        if (!string.IsNullOrEmpty(attrName) && attrName != p.Name)
                        {
                            diagnostics.Add(Diagnostic.Create(
                                BETTA010_NameAttributeOverridesClrName,
                                p.Locations.FirstOrDefault() ?? m.Locations.FirstOrDefault() ?? Location.None,
                                p.Name, typeSym.Name, m.Name, attrName));
                        }
                    }
                }

                Guid guid;
                string guidInput = null;
                if (!string.IsNullOrEmpty(explicitGuid) && Guid.TryParse(explicitGuid, out var parsedGuid))
                {
                    guid = parsedGuid;
                }
                else
                {
                    (guid, guidInput) = ComputeMethodGuidWithInput(serviceFullName, m.Name, parameters, returnFullName);
                }

                var hasPreview = ReturnSurfaces(m.ReturnType, PreviewInterfaceFullName);
                var hasBakeable = ReturnSurfaces(m.ReturnType, BakeableInterfaceFullName);

                methods.Add(new MethodModel(
                    methodName: m.Name,
                    name: name,
                    nickName: nickName,
                    description: desc,
                    category: category,
                    subCategory: subCategory,
                    guid: guid,
                    enabled: enabled,
                    iconResource: iconResource,
                    hasPreview: hasPreview,
                    hasBakeable: hasBakeable,
                    parameters: parameters,
                    guidInputDebug: guidInput));
            }

            if (methods.Count == 0 && diagnostics.Count == 0) return null;

            return new ServiceTypeModel(
                fullName: ReflectionFullName(typeSym),
                methods: methods,
                diagnostics: diagnostics);
        }

        private static bool ImplementsIBettaCollection(INamedTypeSymbol typeSym)
        {
            if (typeSym.TypeKind == TypeKind.Interface)
            {
                if (typeSym.ToDisplayString() == IBettaCollectionFullName) return false;
                foreach (var i in typeSym.AllInterfaces)
                    if (i.ToDisplayString() == IBettaCollectionFullName) return true;
                return false;
            }
            if (typeSym.TypeKind == TypeKind.Class && !typeSym.IsAbstract)
            {
                foreach (var i in typeSym.AllInterfaces)
                    if (i.ToDisplayString() == IBettaCollectionFullName) return true;
            }
            return false;
        }

        // ---- Attribute helpers --------------------------------------------

        private static AttributeData FindAttribute(ImmutableArray<AttributeData> attrs, string simpleName)
        {
            foreach (var a in attrs)
            {
                var n = a.AttributeClass?.Name;
                if (n == null) continue;
                if (n == simpleName) return a;
                if (n == simpleName + "Attribute") return a;
            }
            return null;
        }

        private static string NamedArgString(AttributeData a, string name, string fallback)
        {
            if (a == null) return fallback;
            foreach (var kv in a.NamedArguments)
                if (kv.Key == name)
                    return kv.Value.Value as string ?? fallback;
            return fallback;
        }

        private static bool NamedArgBool(AttributeData a, string name, bool fallback)
        {
            if (a == null) return fallback;
            foreach (var kv in a.NamedArguments)
                if (kv.Key == name && kv.Value.Value is bool b)
                    return b;
            return fallback;
        }

        private static (string Category, string SubCategory) ExtractCollectionDefaults(AttributeData attr, INamedTypeSymbol typeSym)
        {
            string category = null, subCategory = null;
            if (attr != null)
            {
                if (attr.ConstructorArguments.Length >= 1) category = attr.ConstructorArguments[0].Value as string;
                if (attr.ConstructorArguments.Length >= 2) subCategory = attr.ConstructorArguments[1].Value as string;
                category = NamedArgString(attr, "Category", category);
                subCategory = NamedArgString(attr, "SubCategory", subCategory);
            }
            if (string.IsNullOrEmpty(category))
                category = TrimInterfacePrefix(typeSym.Name);
            if (string.IsNullOrEmpty(subCategory))
                subCategory = "General";
            return (category, subCategory);
        }

        private static string TrimInterfacePrefix(string typeName)
        {
            if (!string.IsNullOrEmpty(typeName) && typeName.Length > 1 &&
                typeName[0] == 'I' && char.IsUpper(typeName[1]))
                return typeName.Substring(1);
            return typeName;
        }

        // ---- Preview / Bakeable detection ---------------------------------

        private static bool ReturnSurfaces(ITypeSymbol t, string interfaceFullName)
        {
            var unwrapped = UnwrapTask(t);
            return ContainsInterface(unwrapped, interfaceFullName);
        }

        private static bool ContainsInterface(ITypeSymbol t, string interfaceFullName)
        {
            if (t == null) return false;
            var element = UnwrapListLike(t);
            if (element != null && !SymbolEqualityComparer.Default.Equals(element, t))
                return ContainsInterface(element, interfaceFullName);
            if (t.ToDisplayString() == interfaceFullName) return true;
            foreach (var i in t.AllInterfaces)
                if (i.ToDisplayString() == interfaceFullName) return true;

            if (t is INamedTypeSymbol nt && nt.IsGenericType)
            {
                var def = nt.ConstructedFrom.ToDisplayString();
                if (def.StartsWith("System.Tuple", StringComparison.Ordinal) ||
                    def.StartsWith("System.ValueTuple", StringComparison.Ordinal))
                {
                    foreach (var arg in nt.TypeArguments)
                        if (ContainsInterface(arg, interfaceFullName)) return true;
                }
            }
            return false;
        }

        private static ITypeSymbol UnwrapTask(ITypeSymbol t)
        {
            if (t is not INamedTypeSymbol nt || !nt.IsGenericType) return t;
            var def = nt.ConstructedFrom.ToDisplayString();
            if (def == "System.Threading.Tasks.Task<T>" || def == "System.Threading.Tasks.ValueTask<T>" ||
                def == "System.Threading.Tasks.Task" || def == "System.Threading.Tasks.ValueTask")
            {
                if (nt.TypeArguments.Length > 0) return nt.TypeArguments[0];
            }
            return t;
        }

        private static ITypeSymbol UnwrapListLike(ITypeSymbol t)
        {
            if (t is not INamedTypeSymbol nt) return null;
            if (nt.IsGenericType && nt.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.List<T>")
                return nt.TypeArguments[0];
            // IEnumerable<T>
            foreach (var i in t.AllInterfaces)
            {
                if (i.IsGenericType && i.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>" &&
                    t.SpecialType != SpecialType.System_String)
                    return i.TypeArguments[0];
            }
            return null;
        }

        // ---- Supported-type whitelist (BETTA001) --------------------------

        private static bool IsSupportedParameterType(ITypeSymbol t)
        {
            if (t == null) return false;
            // Primitives + string
            switch (t.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Char:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_String:
                case SpecialType.System_Object:
                case SpecialType.System_DateTime:
                    return true;
            }
            if (t.TypeKind == TypeKind.Enum) return true;

            var ns = NamespaceFullName(t);
            if (ns != null && (
                ns == "Rhino.Geometry" ||
                ns.StartsWith("Rhino.Geometry.", StringComparison.Ordinal) ||
                ns == "Grasshopper.Kernel.Data" ||
                ns.StartsWith("Grasshopper.Kernel.Data.", StringComparison.Ordinal) ||
                ns == "Grasshopper.Kernel.Types" ||
                ns.StartsWith("Grasshopper.Kernel.Types.", StringComparison.Ordinal)))
                return true;

            // Opaque types (IBettaValue or [GrasshopperOpaque] on the type or any interface)
            if (IsOpaque(t)) return true;

            // List<T>
            if (t is INamedTypeSymbol nt1 && nt1.IsGenericType)
            {
                var def = nt1.ConstructedFrom.ToDisplayString();
                if (def == "System.Collections.Generic.List<T>" ||
                    def == "System.Collections.Generic.IEnumerable<T>" ||
                    def == "System.Collections.Generic.IReadOnlyList<T>")
                {
                    return IsSupportedParameterType(nt1.TypeArguments[0]);
                }
                if (def.StartsWith("System.Tuple", StringComparison.Ordinal) ||
                    def.StartsWith("System.ValueTuple", StringComparison.Ordinal))
                {
                    return nt1.TypeArguments.All(IsSupportedParameterType);
                }
                if (def == "System.Threading.Tasks.Task<T>" ||
                    def == "System.Threading.Tasks.ValueTask<T>")
                {
                    return IsSupportedParameterType(nt1.TypeArguments[0]);
                }
            }

            return false;
        }

        private static bool IsOpaque(ITypeSymbol t)
        {
            foreach (var a in t.GetAttributes())
                if (a.AttributeClass?.Name == GrasshopperOpaqueAttr ||
                    a.AttributeClass?.Name == GrasshopperOpaqueAttr + "Attribute") return true;
            foreach (var i in t.AllInterfaces)
            {
                if (i.ToDisplayString() == BettaValueInterfaceFullName) return true;
                foreach (var a in i.GetAttributes())
                    if (a.AttributeClass?.Name == GrasshopperOpaqueAttr ||
                        a.AttributeClass?.Name == GrasshopperOpaqueAttr + "Attribute") return true;
            }
            return false;
        }

        private static string NamespaceFullName(ITypeSymbol t)
        {
            if (t.ContainingNamespace == null || t.ContainingNamespace.IsGlobalNamespace) return null;
            return t.ContainingNamespace.ToDisplayString();
        }

        // ---- Reflection-style FullName reproduction -----------------------

        /// <summary>
        /// Reproduce reflection's <see cref="Type.FullName"/> from a Roslyn
        /// symbol. The runtime hashes <c>method.ParameterType.FullName</c> and
        /// <c>method.ReturnType.FullName</c> into the descriptor GUID, so the
        /// generator must match those strings character-for-character or saved
        /// .gh documents would mis-resolve.
        ///
        /// Examples (target == output of <c>Type.FullName</c>):
        ///   System.Double                                   → "System.Double"
        ///   System.Collections.Generic.List&lt;double&gt;   → "System.Collections.Generic.List`1[[System.Double, System.Private.CoreLib, Version=..., ...]]"
        ///   Nested+Inner generic                            → "Outer+Inner`1[[T-FullName]]"
        ///
        /// For determinism we mirror the .NET 7 / CoreCLR convention: closed
        /// generics include the assembly-qualified name of each type argument
        /// in brackets. The runtime's existing reflection call produces that
        /// exact string, so we replicate it here.
        /// </summary>
        internal static string ReflectionFullName(ITypeSymbol t)
        {
            if (t == null) return null;
            if (t is IArrayTypeSymbol arr)
            {
                var elt = ReflectionFullName(arr.ElementType);
                var ranks = arr.Rank == 1 ? "[]" : "[" + new string(',', arr.Rank - 1) + "]";
                return elt + ranks;
            }
            if (t is INamedTypeSymbol nt)
            {
                if (nt.IsGenericType && !nt.IsUnboundGenericType)
                {
                    var def = nt.ConstructedFrom;
                    var defFull = NonGenericFullName(def);
                    var args = nt.TypeArguments.Select(a => "[" + ReflectionAssemblyQualifiedName(a) + "]");
                    return defFull + "[" + string.Join(",", args) + "]";
                }
                return NonGenericFullName(nt);
            }
            // Type parameters, dynamic, etc — fall back to display string
            return t.ToDisplayString();
        }

        private static string NonGenericFullName(INamedTypeSymbol nt)
        {
            // Build namespace + nested chain "Outer+Inner+...`Arity"
            var nameStack = new Stack<string>();
            INamedTypeSymbol cur = nt;
            while (cur != null)
            {
                var n = cur.MetadataName; // metadata name includes `Arity for generics
                nameStack.Push(n);
                cur = cur.ContainingType;
            }
            var ns = nt.ContainingNamespace != null && !nt.ContainingNamespace.IsGlobalNamespace
                ? nt.ContainingNamespace.ToDisplayString()
                : null;
            var nested = string.Join("+", nameStack);
            return ns != null ? ns + "." + nested : nested;
        }

        // .NET reference assemblies that all forward to System.Private.CoreLib
        // at runtime. When emitting the assembly identity for a type that
        // Roslyn sees in one of these, we substitute the runtime identity so
        // the generated FullName string matches what Type.FullName would
        // produce at runtime. Without this map the hash drifts on every
        // generic type whose ref assembly is "System.Collections" /
        // "System.Runtime" / "netstandard" / ... — which is most of them.
        private static readonly HashSet<string> _coreRefAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "mscorlib",
            "netstandard",
            "System.Runtime",
            "System.Collections",
            "System.Collections.Generic",
            "System.Collections.Concurrent",
            "System.Collections.Immutable",
            "System.Collections.NonGeneric",
            "System.Collections.Specialized",
            "System.Linq",
            "System.ObjectModel",
            "System.Memory",
            "System.Numerics",
            "System.Numerics.Vectors",
            "System.Threading",
            "System.Threading.Tasks",
            "System.Threading.Tasks.Extensions",
            "System.Threading.Tasks.Parallel",
            "System.Threading.Thread",
            "System.Threading.ThreadPool",
            "System.IO",
            "System.Text.Encoding",
            "System.Text.Encoding.Extensions",
            "System.ValueTuple",
            "System.Console",
            "System.Diagnostics.Debug",
            "System.Diagnostics.Tools",
            "System.Reflection",
            "System.Reflection.Primitives",
            "System.Resources.ResourceManager",
            "System.Runtime.Extensions",
            "System.Runtime.InteropServices",
        };

        private const string CoreLibAsmName = "System.Private.CoreLib";
        private const string CoreLibVersion = "7.0.0.0";
        private const string CoreLibToken = "7cec85d7bea7798e";

        private static string ReflectionAssemblyQualifiedName(ITypeSymbol t)
        {
            var full = ReflectionFullName(t);
            var asm = t.ContainingAssembly;
            if (asm == null) return full;
            var id = asm.Identity;

            // Build the identity string. Types in well-known core ref assemblies
            // forward to System.Private.CoreLib at runtime, so reflection emits
            // that identity in Type.FullName / Type.AssemblyQualifiedName. We
            // mirror that mapping here so the generated and reflection paths
            // hash to the same bytes.
            var sb = new StringBuilder();
            sb.Append(full);
            sb.Append(", ");
            if (_coreRefAssemblyNames.Contains(id.Name))
            {
                sb.Append(CoreLibAsmName);
                sb.Append(", Version=").Append(CoreLibVersion);
                sb.Append(", Culture=neutral");
                sb.Append(", PublicKeyToken=").Append(CoreLibToken);
            }
            else
            {
                sb.Append(id.Name);
                sb.Append(", Version=");
                sb.Append(id.Version);
                sb.Append(", Culture=");
                sb.Append(string.IsNullOrEmpty(id.CultureName) ? "neutral" : id.CultureName);
                sb.Append(", PublicKeyToken=");
                var pkt = id.PublicKeyToken;
                if (pkt.IsDefaultOrEmpty) sb.Append("null");
                else
                {
                    foreach (var b in pkt) sb.Append(b.ToString("x2"));
                }
            }
            return sb.ToString();
        }

        // ---- Compile-time GUID --------------------------------------------

        /// <summary>
        /// Reproduces <c>ComponentDescriptor.GenerateGuidFromMethod</c> exactly:
        ///   paramSig = ",".join(p.ParameterType.FullName + " " + p.Name)
        ///   input    = $"{serviceFullName}|{methodName}|{paramSig}|{returnFullName}"
        ///   guid     = new Guid(MD5(UTF8(input)))
        /// </summary>
        internal static Guid ComputeMethodGuid(string serviceFullName, string methodName,
            IReadOnlyList<ParamModel> parameters, string returnFullName)
        {
            return ComputeMethodGuidWithInput(serviceFullName, methodName, parameters, returnFullName).Guid;
        }

        internal static (Guid Guid, string Input) ComputeMethodGuidWithInput(string serviceFullName, string methodName,
            IReadOnlyList<ParamModel> parameters, string returnFullName)
        {
            var paramSig = string.Join(",", parameters.Select(p => p.TypeFullName + " " + p.Name));
            var input = serviceFullName + "|" + methodName + "|" + paramSig + "|" + returnFullName;
            using var md5 = MD5.Create();
            var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            return (new Guid(bytes), input);
        }

        // ---- Emit ---------------------------------------------------------

        private static string EmitBootstrap(IEnumerable<ServiceTypeModel> types)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/> Betta source generator.");
            sb.AppendLine("// Compile-time descriptor blueprints. The plugin DLL does not need");
            sb.AppendLine("// to reference Betta.gha — the runtime duck-types the blueprint shape");
            sb.AppendLine("// via property reflection in ComponentDescriptorBlueprint.ConvertToDescriptors.");
            sb.AppendLine("#nullable disable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine("namespace Betta.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Local mirror of <c>Betta.Services.ComponentDescriptorBlueprint</c>.");
            sb.AppendLine("    /// Property names + types are matched at startup via reflection so the");
            sb.AppendLine("    /// plugin assembly does not need a direct dependency on Betta.gha.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    internal sealed class BettaGeneratedBlueprint");
            sb.AppendLine("    {");
            sb.AppendLine("        public string ServiceTypeName { get; set; }");
            sb.AppendLine("        public string MethodName { get; set; }");
            sb.AppendLine("        public string[] ParameterTypeNames { get; set; }");
            sb.AppendLine("        public string Name { get; set; }");
            sb.AppendLine("        public string NickName { get; set; }");
            sb.AppendLine("        public string Description { get; set; }");
            sb.AppendLine("        public string Category { get; set; }");
            sb.AppendLine("        public string SubCategory { get; set; }");
            sb.AppendLine("        public System.Guid Guid { get; set; }");
            sb.AppendLine("        public bool Enabled { get; set; }");
            sb.AppendLine("        public string IconResource { get; set; }");
            sb.AppendLine("        public bool HasPreview { get; set; }");
            sb.AppendLine("        public bool HasBakeable { get; set; }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Auto-generated bootstrap. <see cref=\"GetDescriptorBlueprints\"/>");
            sb.AppendLine("    /// returns one blueprint per <c>[GrasshopperMethod]</c> discovered");
            sb.AppendLine("    /// at compile time. Betta's runtime calls this in place of the");
            sb.AppendLine("    /// reflection scan for assemblies that ship the generator.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static partial class BettaPluginBootstrap");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Returns the generated blueprints as <c>System.Object</c>; the");
            sb.AppendLine("        /// runtime reads their properties via reflection. Boxing the");
            sb.AppendLine("        /// return type keeps us free of a Betta.gha reference.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public static IReadOnlyList<object> GetDescriptorBlueprints()");
            sb.AppendLine("        {");
            sb.AppendLine("            var list = new List<object>();");

            foreach (var t in types.OrderBy(x => x.FullName, StringComparer.Ordinal))
            {
                foreach (var m in t.Methods.OrderBy(x => x.MethodName, StringComparer.Ordinal))
                {
                    // Debug aid (commented out): emit the exact compile-time
                    // GUID input string so divergence from reflection's path is
                    // visible during development.
                    //   // GuidInput: "<service>|<method>|<param-sig>|<ret>"
                    // The runtime re-derives the GUID from the resolved
                    // MethodInfo anyway, so a mismatch here is informational
                    // only — it doesn't break save/reload.
                    sb.AppendLine("            list.Add(new BettaGeneratedBlueprint");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                ServiceTypeName = {Literal(t.FullName)},");
                    sb.AppendLine($"                MethodName = {Literal(m.MethodName)},");
                    sb.Append("                ParameterTypeNames = new[] { ");
                    sb.Append(string.Join(", ", m.Parameters.Select(p => Literal(p.TypeFullName))));
                    sb.AppendLine(" },");
                    sb.AppendLine($"                Name = {Literal(m.Name)},");
                    sb.AppendLine($"                NickName = {Literal(m.NickName)},");
                    sb.AppendLine($"                Description = {Literal(m.Description)},");
                    sb.AppendLine($"                Category = {Literal(m.Category)},");
                    sb.AppendLine($"                SubCategory = {Literal(m.SubCategory)},");
                    sb.AppendLine($"                Guid = new System.Guid(\"{m.Guid:D}\"),");
                    sb.AppendLine($"                Enabled = {(m.Enabled ? "true" : "false")},");
                    sb.AppendLine($"                IconResource = {Literal(m.IconResource)},");
                    sb.AppendLine($"                HasPreview = {(m.HasPreview ? "true" : "false")},");
                    sb.AppendLine($"                HasBakeable = {(m.HasBakeable ? "true" : "false")},");
                    sb.AppendLine("            });");
                }
            }

            sb.AppendLine("            return list;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string Literal(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        // ---- Models -------------------------------------------------------

        internal sealed class ServiceTypeModel
        {
            public string FullName { get; }
            public List<MethodModel> Methods { get; }
            public List<Diagnostic> Diagnostics { get; }

            public ServiceTypeModel(string fullName, List<MethodModel> methods, List<Diagnostic> diagnostics)
            {
                FullName = fullName;
                Methods = methods;
                Diagnostics = diagnostics;
            }
        }

        internal sealed class MethodModel
        {
            public string MethodName { get; }
            public string Name { get; }
            public string NickName { get; }
            public string Description { get; }
            public string Category { get; }
            public string SubCategory { get; }
            public Guid Guid { get; }
            public bool Enabled { get; }
            public string IconResource { get; }
            public bool HasPreview { get; }
            public bool HasBakeable { get; }
            public ParamModel[] Parameters { get; }
            public string GuidInputDebug { get; }

            public MethodModel(string methodName, string name, string nickName, string description,
                string category, string subCategory, Guid guid, bool enabled, string iconResource,
                bool hasPreview, bool hasBakeable, ParamModel[] parameters, string guidInputDebug = null)
            {
                MethodName = methodName;
                Name = name;
                NickName = nickName;
                Description = description;
                Category = category;
                SubCategory = subCategory;
                Guid = guid;
                Enabled = enabled;
                IconResource = iconResource;
                HasPreview = hasPreview;
                HasBakeable = hasBakeable;
                Parameters = parameters;
                GuidInputDebug = guidInputDebug;
            }
        }

        internal sealed class ParamModel
        {
            public string Name { get; }
            public string TypeFullName { get; }
            public ParamModel(string name, string typeFullName) { Name = name; TypeFullName = typeFullName; }
        }
    }
}
