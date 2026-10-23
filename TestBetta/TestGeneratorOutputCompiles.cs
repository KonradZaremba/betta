// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Betta.Attributes;
using Betta.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// The generator's bootstrap is compiled into the plugin, so it must compile wherever the
    /// plugin does: under the plugin's C# version, and whatever its methods and attribute
    /// strings look like.
    /// </summary>
    public class TestGeneratorOutputCompiles
    {
        private const string Source = @"
using Betta.Attributes;
using Betta.Interfaces;

namespace EmitProbe
{
    public interface IProbe : IBettaCollection
    {
        [GrasshopperMethod(""Now"")] double Now();
        [GrasshopperMethod(""Two\nLines"", Description = ""Tab\there, quote \"" and backslash \\ and é"")] double Text(double x);
    }
}";

        private static IEnumerable Blueprints(LanguageVersion version)
        {
            var parse = new CSharpParseOptions(version);
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Select(p => MetadataReference.CreateFromFile(p))
                .Append(MetadataReference.CreateFromFile(typeof(GrasshopperMethodAttribute).Assembly.Location));
            var compilation = CSharpCompilation.Create("EmitProbe_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(Source, parse) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            CSharpGeneratorDriver.Create(new[] { new BettaComponentGenerator().AsSourceGenerator() }, parseOptions: parse)
                .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
            using var pe = new MemoryStream();
            var emit = output.Emit(pe);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

            return (IEnumerable)Assembly.Load(pe.ToArray()).GetType("Betta.Generated.BettaPluginBootstrap")
                .GetMethod("GetDescriptorBlueprints").Invoke(null, null);
        }

        private static object Prop(object bp, string name) => bp.GetType().GetProperty(name).GetValue(bp);

        [Theory]
        [InlineData(LanguageVersion.CSharp7_3)]
        [InlineData(LanguageVersion.CSharp11)]
        public void The_bootstrap_compiles_and_keeps_every_string_exactly(LanguageVersion version)
        {
            var blueprints = Blueprints(version).Cast<object>().ToList();

            var now = blueprints.Single(b => (string)Prop(b, "MethodName") == "Now");
            Assert.Empty((string[])Prop(now, "ParameterTypeNames"));

            var text = blueprints.Single(b => (string)Prop(b, "MethodName") == "Text");
            Assert.Equal("Two\nLines", Prop(text, "Name"));
            Assert.Equal("Tab\there, quote \" and backslash \\ and é", Prop(text, "Description"));
        }
    }
}
