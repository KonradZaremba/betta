// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Betta.Attributes;
using Betta.Generators;
using Betta.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// The source generator computes component GUIDs at compile time; the runtime computes them
    /// from the loaded MethodInfo. Both must agree, overloads and generic signatures included,
    /// and a refactor that changes only parameters and outputs must keep the GUID. Compiles
    /// snippets with the generator in memory: no Rhino needed.
    /// </summary>
    public class TestGeneratorStableGuids
    {
        private const string Source = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Betta.Attributes;
using Betta.Interfaces;

namespace GenProbe
{
    public class Probe : IBettaCollection
    {
        [GrasshopperMethod(""Plain"")] public double Plain(double x) => x;
        [GrasshopperMethod(""Batch"")] public List<double> Batch(List<double> xs) => xs;
        [GrasshopperMethod(""Pair"")] public Task<(double Sum, double Mean)> Pair(double[] xs) => Task.FromResult((0.0, 0.0));
        [GrasshopperMethod(""Nested"")] public Dictionary<string, List<int>> Nested(int[,] grid) => null;
        [GrasshopperMethod(""Nine"")] public (int, int, int, int, int, int, int, int, int) Nine(int a) => default;
        [GrasshopperMethod(""Scale Number"")] public double Scale(double x, double f) => x;
        [GrasshopperMethod(""Scale List"")] public List<double> Scale(List<double> xs, double f) => xs;
    }
}";

        // The benchmark's refactor: rename an input, add one, add an output, move the subcategory.
        private const string RemapBefore = @"
using Betta.Attributes;
using Betta.Interfaces;

namespace GenProbe
{
    [GrasshopperCollection(""Bench"", ""Math"")]
    public interface IRemap : IBettaCollection
    {
        [GrasshopperMethod(""Remap"")]
        double Remap([GrasshopperParameter(""Value"")] double value, double sourceMin, double sourceMax, double targetMin, double targetMax);
    }
}";

        private const string RemapAfter = @"
using Betta.Attributes;
using Betta.Interfaces;

namespace GenProbe
{
    [GrasshopperCollection(""Bench"", ""Mapping"")]
    public interface IRemap : IBettaCollection
    {
        [GrasshopperMethod(""Remap"")]
        (double Result, double Normalized) Remap([GrasshopperParameter(""Number"")] double number, double sourceMin, double sourceMax,
            double targetMin, double targetMax, bool clamp = false);
    }
}";

        private const string RemapRenamed = @"
using Betta.Attributes;
using Betta.Interfaces;

namespace GenProbe
{
    public interface IRemap : IBettaCollection
    {
        [GrasshopperMethod(""Remap"")]
        double Map(double value, double sourceMin, double sourceMax, double targetMin, double targetMax);
    }
}";

        private static (Assembly Assembly, IEnumerable Blueprints, IReadOnlyList<Diagnostic> Diagnostics) Compile(string source)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Select(p => MetadataReference.CreateFromFile(p))
                .Append(MetadataReference.CreateFromFile(typeof(GrasshopperMethodAttribute).Assembly.Location));
            var compilation = CSharpCompilation.Create("GenProbe_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            CSharpGeneratorDriver.Create(new BettaComponentGenerator())
                .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
            using var pe = new MemoryStream();
            var emit = output.Emit(pe);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

            var asm = Assembly.Load(pe.ToArray());
            var blueprints = (IEnumerable)asm.GetType("Betta.Generated.BettaPluginBootstrap")
                .GetMethod("GetDescriptorBlueprints").Invoke(null, null);
            return (asm, blueprints, diagnostics);
        }

        private static object Prop(object bp, string name) => bp.GetType().GetProperty(name).GetValue(bp);

        [Fact]
        public void Generator_and_runtime_derive_the_same_guids_and_type_names()
        {
            var (asm, blueprints, diagnostics) = Compile(Source);
            var probe = asm.GetType("GenProbe.Probe");

            var checkedMethods = 0;
            foreach (var bp in blueprints)
            {
                var names = (string[])Prop(bp, "ParameterTypeNames");
                var method = probe.GetMethods().Single(m => m.Name == (string)Prop(bp, "MethodName") &&
                    m.GetParameters().Select(p => ComponentDescriptor.StableTypeName(p.ParameterType)).SequenceEqual(names));

                Assert.Equal(ComponentDescriptor.GenerateGuidFromMethod(probe, method), (Guid)Prop(bp, "Guid"));
                checkedMethods++;
            }
            Assert.Equal(7, checkedMethods);
            Assert.Equal(2, diagnostics.Count(d => d.Id == "BETTA011"));
        }

        [Fact]
        public void Changing_parameters_and_outputs_keeps_the_guid_renaming_the_method_does_not()
        {
            Guid Runtime(string source)
            {
                var (asm, blueprints, _) = Compile(source);
                var type = asm.GetType("GenProbe.IRemap");
                var bp = blueprints.Cast<object>().Single();
                var guid = ComponentDescriptor.GenerateGuidFromMethod(type, type.GetMethod((string)Prop(bp, "MethodName")));
                Assert.Equal(guid, (Guid)Prop(bp, "Guid"));
                return guid;
            }

            var before = Runtime(RemapBefore);
            Assert.Equal(before, Runtime(RemapAfter));
            Assert.NotEqual(before, Runtime(RemapRenamed));
        }
    }
}
