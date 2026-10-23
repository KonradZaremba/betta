// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Betta.Attributes;
using Betta.Generators;
using Betta.Generators.CodeFixes;
using Betta.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using static TestBetta.TestGuidLock;

namespace TestBetta
{
    /// <summary>
    /// The BETTA012 quick fix, applied as the IDE would: lock what the first build would write,
    /// change the code, take the warning the generator reports, apply its fix in an in-memory
    /// workspace, and run the generator on the result. The warning must be gone, and the runtime
    /// must answer to the locked GUID.
    /// </summary>
    public class TestGuidLockCodeFix
    {
        private static async Task<(string Text, string Title)> Fix(string source, string lockText)
        {
            using var workspace = new AdhocWorkspace();
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Select(p => MetadataReference.CreateFromFile(p))
                .Append(MetadataReference.CreateFromFile(typeof(GrasshopperMethodAttribute).Assembly.Location));
            var document = workspace.CurrentSolution
                .AddProject("LockProbe", "LockProbe", LanguageNames.CSharp)
                .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .WithMetadataReferences(references)
                .AddDocument("Pack.cs", source);

            var compilation = await document.Project.GetCompilationAsync();
            CSharpGeneratorDriver.Create(new[] { new BettaComponentGenerator().AsSourceGenerator() },
                    new AdditionalText[] { new Text(LockPath, lockText) })
                .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
            var diagnostic = Assert.Single(diagnostics, d => d.Id == "BETTA012");

            var actions = new List<CodeAction>();
            await new GuidLockCodeFix().RegisterCodeFixesAsync(
                new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
            if (actions.Count == 0) return (null, null);

            var fix = Assert.Single(actions);
            var operations = await fix.GetOperationsAsync(CancellationToken.None);
            var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id);
            return ((await changed.GetTextAsync()).ToString(), fix.Title);
        }

        private const string Map = @"[GrasshopperMethod(""Remap"")] double Map(double value);";

        [Fact]
        public async Task A_renamed_method_gets_its_old_name_in_FormerNames()
        {
            var lockText = Generate(Pack(Remap)).Lock();

            var (text, title) = await Fix(Pack(Map), lockText);
            Assert.Equal("Add \"Remap\" to FormerNames", title);
            Assert.Contains(@"[GrasshopperMethod(""Remap"", FormerNames = new[] { ""Remap"" })] double Map(double value);", text);

            var fixedRun = Generate(text, lockText);
            Assert.Empty(fixedRun.Gone);
            var type = Load(fixedRun.Output, "LockProbe.IMathPack");
            Assert.Contains(LockedGuid(lockText, "LockProbe.IMathPack.Remap"),
                ComponentDescriptor.FormerGuidsFromAttributes(type, type.GetMethod("Map")));
        }

        [Fact]
        public async Task A_method_with_FormerNames_already_gets_one_more()
        {
            const string before = @"[GrasshopperMethod(""Remap"", FormerNames = new[] { ""Scale"" })] double Remap(double value);";
            var lockText = Generate(Pack(before)).Lock();

            var (text, _) = await Fix(Pack(@"[GrasshopperMethod(""Remap"", FormerNames = new[] { ""Scale"" })] double Map(double value);"), lockText);
            Assert.Contains(@"FormerNames = new[] { ""Scale"", ""Remap"" }", text);
            Assert.Empty(Generate(text, lockText).Gone);
        }

        [Fact]
        public async Task A_method_moved_to_another_type_gets_its_old_full_name()
        {
            string Two(string math, string geo) => Pack(math) + Pack(geo, name: "IGeoPack");
            const string area = @"[GrasshopperMethod(""Area"")] double Area(double r);";
            var lockText = Generate(Two(Remap + Clamp, area)).Lock();

            var (text, title) = await Fix(Two(Clamp, area + Remap), lockText);
            Assert.Equal("Add \"LockProbe.IMathPack.Remap\" to FormerNames", title);
            Assert.Empty(Generate(text, lockText).Gone);
        }

        [Fact]
        public async Task A_renamed_type_gets_its_old_name_on_GrasshopperCollection()
        {
            var lockText = Generate(Pack(Remap)).Lock();

            var (text, title) = await Fix(Pack(Remap, name: "IMathTools"), lockText);
            Assert.Equal("Add \"LockProbe.IMathPack\" to FormerNames on [GrasshopperCollection]", title);
            Assert.Contains(@"[GrasshopperCollection(""Probe"", ""Math"", FormerNames = new[] { ""LockProbe.IMathPack"" })]", text);
            Assert.Empty(Generate(text, lockText).Gone);
        }

        [Fact]
        public async Task A_renamed_type_without_GrasshopperCollection_gets_one_on_its_own_line()
        {
            var lockText = Generate(Pack(Remap, collection: "/// <summary>Maths.</summary>")).Lock();

            var (text, _) = await Fix(Pack(Remap, collection: "/// <summary>Maths.</summary>", name: "IMathTools"), lockText);
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            var at = lines.FindIndex(l => l.Contains("[GrasshopperCollection("));
            Assert.Equal(@"    [GrasshopperCollection(FormerNames = new[] { ""LockProbe.IMathPack"" })]", lines[at]);
            Assert.Equal("    /// <summary>Maths.</summary>", lines[at - 1]);
            Assert.Equal("    public interface IMathTools : IBettaCollection", lines[at + 1]);
            Assert.Empty(Generate(text, lockText).Gone);
        }

        [Fact]
        public async Task A_published_method_pinned_to_a_new_guid_gets_its_old_one_back()
        {
            var lockText = Generate(Pack(Remap)).Lock();
            var old = LockedGuid(lockText, "LockProbe.IMathPack.Remap");

            var (text, title) = await Fix(Pack(@"[GrasshopperMethod(""Remap"", Guid = ""1b4e28ba-2fa1-11d2-883f-0016d3cca427"")] double Remap(double value);"), lockText);
            Assert.Equal($"Pin Guid = \"{old:D}\"", title);
            Assert.Contains($@"Guid = ""{old:D}""", text);
            Assert.Empty(Generate(text, lockText).Gone);
        }

        [Fact]
        public async Task A_component_with_no_likely_fix_offers_none()
        {
            var lockText = Generate(Pack(Remap + Clamp)).Lock();
            var (text, _) = await Fix(Pack(Clamp), lockText);
            Assert.Null(text);
        }
    }
}
