// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Betta.Attributes;
using Betta.Generators;
using Betta.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// betta.lock: the build records each component's GUID, and the generator warns (BETTA012)
    /// when a later change leaves a locked GUID that nothing answers to. Each test locks what the
    /// first build would write, changes the code, and runs the generator again, as the next build
    /// would. The last test runs real <c>dotnet build</c>s, for the targets that write the file.
    /// </summary>
    public class TestGuidLock
    {
        internal const string LockPath = @"C:\probe\betta.lock";

        // Usings inside the namespace, so two packs can share one file.
        internal static string Pack(string members, string collection = @"[GrasshopperCollection(""Probe"", ""Math"")]", string name = "IMathPack") => $@"
namespace LockProbe
{{
    using System.Collections.Generic;
    using Betta.Attributes;
    using Betta.Interfaces;

    {collection}
    public interface {name} : IBettaCollection
    {{
        {members}
    }}
}}";

        internal const string Remap = @"[GrasshopperMethod(""Remap"")] double Remap(double value);";
        internal const string Clamp = @"[GrasshopperMethod(""Clamp"")] double Clamp(double value);";

        internal sealed class Run
        {
            public ImmutableArray<Diagnostic> Diagnostics { get; set; }
            public string Pending { get; set; }
            public Compilation Output { get; set; }
            public List<Diagnostic> Gone => Diagnostics.Where(d => d.Id == "BETTA012").ToList();

            /// <summary>The "guid Type.Method" lines the build would append.</summary>
            public List<string> Added => (Pending ?? "").Split('\n').Select(l => l.TrimEnd('\r'))
                .Where(l => l.StartsWith("// add: ")).Select(l => l.Substring(8)).ToList();

            /// <summary>The lock as the build leaves it: <paramref name="existing"/> plus what was added.</summary>
            public string Lock(string existing = "") => existing + string.Concat(Added.Select(l => l + "\n"));
        }

        internal static Run Generate(string source, string lockText = null, bool lockOff = false)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Select(p => MetadataReference.CreateFromFile(p))
                .Append(MetadataReference.CreateFromFile(typeof(GrasshopperMethodAttribute).Assembly.Location));
            var compilation = CSharpCompilation.Create("LockProbe_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var additional = lockText == null ? Array.Empty<AdditionalText>() : new AdditionalText[] { new Text(LockPath, lockText) };
            var options = new Options(lockOff ? "false" : "true");
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new BettaComponentGenerator().AsSourceGenerator() },
                additional, optionsProvider: options);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

            var pending = driver.GetRunResult().Results.Single().GeneratedSources
                .Where(s => s.HintName == "BettaLockPending.g.cs").Select(s => s.SourceText.ToString()).SingleOrDefault();
            return new Run { Diagnostics = diagnostics, Pending = pending, Output = output };
        }

        internal static Type Load(Compilation output, string typeName)
        {
            using var pe = new MemoryStream();
            var emit = output.Emit(pe);
            Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            return Assembly.Load(pe.ToArray()).GetType(typeName);
        }

        internal static Guid LockedGuid(string lockText, string label) =>
            Guid.Parse(lockText.Split('\n').Single(l => l.EndsWith(" " + label)).Substring(0, 36));

        [Fact]
        public void The_first_build_lists_every_published_component()
        {
            var run = Generate(Pack(Remap + Clamp + @"
                [GrasshopperMethod(""Draft"", Enabled = false)] double Draft(double value);
                [GrasshopperMethod(""Scale Number"")] double Scale(double x, double f);
                [GrasshopperMethod(""Scale List"")] List<double> Scale(List<double> xs, double f);"));

            Assert.Empty(run.Gone);
            // A disabled method is never published, so no saved file can hold it.
            Assert.Equal(new[]
            {
                "LockProbe.IMathPack.Clamp",
                "LockProbe.IMathPack.Remap",
                "LockProbe.IMathPack.Scale(System.Collections.Generic.List`1[System.Double], System.Double)",
                "LockProbe.IMathPack.Scale(System.Double, System.Double)",
            }, run.Added.Select(l => l.Substring(37)));
        }

        [Fact]
        public void A_renamed_method_is_reported_on_the_new_name_and_FormerNames_clears_it()
        {
            var lockText = Generate(Pack(Remap)).Lock();

            var renamed = Generate(Pack(@"[GrasshopperMethod(""Remap"")] double Map(double value);"), lockText);
            var gone = Assert.Single(renamed.Gone);
            Assert.Contains("'LockProbe.IMathPack.Remap' is in betta.lock", gone.GetMessage());
            Assert.Contains(@"renamed to 'Map', add FormerNames = new[] { ""Remap"" }", gone.GetMessage());
            Assert.Equal("Map", gone.Location.SourceTree.GetText().ToString(gone.Location.SourceSpan));
            Assert.Equal(LockPath, gone.AdditionalLocations.Single().GetLineSpan().Path);
            // The lock doesn't take in the new GUID until the rename is resolved.
            Assert.Empty(renamed.Added);

            var kept = Generate(Pack(@"[GrasshopperMethod(""Remap"", FormerNames = new[] { ""Remap"" })] double Map(double value);"), lockText);
            Assert.Empty(kept.Gone);
            Assert.EndsWith(" LockProbe.IMathPack.Map", Assert.Single(kept.Added));

            // The runtime registers the locked GUID as an alias, so saved files really do open.
            var type = Load(kept.Output, "LockProbe.IMathPack");
            Assert.Contains(LockedGuid(lockText, "LockProbe.IMathPack.Remap"),
                ComponentDescriptor.FormerGuidsFromAttributes(type, type.GetMethod("Map")));
        }

        [Fact]
        public void A_renamed_type_points_at_the_collection_to_fix()
        {
            var lockText = Generate(Pack(Remap + Clamp)).Lock();

            var renamed = Generate(Pack(Remap + Clamp, name: "IMathTools"), lockText);
            Assert.Equal(2, renamed.Gone.Count);
            Assert.All(renamed.Gone, d => Assert.Contains(
                @"If 'LockProbe.IMathPack' was renamed or moved to 'LockProbe.IMathTools', add FormerNames = new[] { ""LockProbe.IMathPack"" } to the [GrasshopperCollection]",
                d.GetMessage()));

            var kept = Generate(Pack(Remap + Clamp, name: "IMathTools",
                collection: @"[GrasshopperCollection(""Probe"", ""Math"", FormerNames = new[] { ""LockProbe.IMathPack"" })]"), lockText);
            Assert.Empty(kept.Gone);
            Assert.Equal(2, kept.Added.Count);

            var type = Load(kept.Output, "LockProbe.IMathTools");
            Assert.Contains(LockedGuid(lockText, "LockProbe.IMathPack.Clamp"),
                ComponentDescriptor.FormerGuidsFromAttributes(type, type.GetMethod("Clamp")));
        }

        [Fact]
        public void A_method_moved_to_another_type_names_its_old_full_name()
        {
            string Two(string math, string geo) => Pack(math) + Pack(geo, name: "IGeoPack");
            var area = @"[GrasshopperMethod(""Area"")] double Area(double r);";
            var lockText = Generate(Two(Remap + Clamp, area)).Lock();

            var moved = Generate(Two(Clamp, area + Remap), lockText);
            var gone = Assert.Single(moved.Gone);
            Assert.Contains(@"If it moved to 'LockProbe.IGeoPack', add FormerNames = new[] { ""LockProbe.IMathPack.Remap"" }", gone.GetMessage());

            var kept = Generate(Two(Clamp, area + @"[GrasshopperMethod(""Remap"", FormerNames = new[] { ""LockProbe.IMathPack.Remap"" })] double Remap(double value);"), lockText);
            Assert.Empty(kept.Gone);
        }

        [Fact]
        public void Adding_an_overload_is_reported_until_the_first_one_lists_its_own_name()
        {
            var lockText = Generate(Pack(@"[GrasshopperMethod(""Scale Number"")] double Scale(double x, double f);")).Lock();
            const string overload = @"[GrasshopperMethod(""Scale List"")] List<double> Scale(List<double> xs, double f);";

            var added = Generate(Pack(@"[GrasshopperMethod(""Scale Number"")] double Scale(double x, double f);" + overload), lockText);
            Assert.Contains("Adding an overload of 'Scale' changed its GUID", Assert.Single(added.Gone).GetMessage());

            var kept = Generate(Pack(@"[GrasshopperMethod(""Scale Number"", FormerNames = new[] { ""Scale"" })] double Scale(double x, double f);" + overload), lockText);
            Assert.Empty(kept.Gone);
            Assert.Equal(2, kept.Diagnostics.Count(d => d.Id == "BETTA011"));
        }

        [Fact]
        public void A_removed_component_is_reported_on_its_lock_line_until_the_line_goes()
        {
            var lines = Generate(Pack(Remap + Clamp)).Added;
            var remapLine = lines.Single(l => l.EndsWith(".Remap"));
            var lockText = "# comment\n\nnot a guid line\n" + string.Join("\n", lines) + "\n";

            var removed = Generate(Pack(Clamp), lockText);
            var gone = Assert.Single(removed.Gone);
            Assert.Contains("If it was renamed or moved, list its old name in FormerNames", gone.GetMessage());
            var span = gone.Location.GetLineSpan();
            Assert.Equal(LockPath, span.Path);
            Assert.Equal(3 + lines.IndexOf(remapLine), span.StartLinePosition.Line);

            Assert.Empty(Generate(Pack(Clamp), lockText.Replace(remapLine + "\n", "")).Gone);
        }

        [Fact]
        public void A_pinned_guid_keeps_a_rename_quiet()
        {
            const string pin = @"Guid = ""1b4e28ba-2fa1-11d2-883f-0016d3cca427""";
            var lockText = Generate(Pack($@"[GrasshopperMethod(""Remap"", {pin})] double Remap(double value);")).Lock();
            Assert.StartsWith("1b4e28ba-2fa1-11d2-883f-0016d3cca427 ", lockText);

            var renamed = Generate(Pack($@"[GrasshopperMethod(""Remap"", {pin})] double Map(double value);"), lockText);
            Assert.Empty(renamed.Gone);
            Assert.Empty(renamed.Added);
        }

        [Fact]
        public void Pinning_a_published_method_to_a_new_guid_says_to_pin_the_old_one()
        {
            var lockText = Generate(Pack(Remap)).Lock();
            var old = LockedGuid(lockText, "LockProbe.IMathPack.Remap");

            var pinned = Generate(Pack(@"[GrasshopperMethod(""Remap"", Guid = ""1b4e28ba-2fa1-11d2-883f-0016d3cca427"")] double Remap(double value);"), lockText);
            var gone = Assert.Single(pinned.Gone);
            Assert.Contains($@"pin Guid = ""{old:D}"" instead, or remove the pin", gone.GetMessage());
            Assert.Equal("Remap", gone.Location.SourceTree.GetText().ToString(gone.Location.SourceSpan));
        }

        [Fact]
        public void BettaLock_false_turns_the_lock_off()
        {
            var lockText = Generate(Pack(Remap)).Lock();
            var off = Generate(Pack(Clamp), lockText, lockOff: true);
            Assert.Empty(off.Gone);
            Assert.Null(off.Pending);
        }

        /// <summary>
        /// The targets end to end: the first build writes betta.lock, a rename gets BETTA012 and adds
        /// nothing, and FormerNames clears the warning and adds the new name. Builds a throwaway
        /// project outside the repo with the generator and abstractions next to this test.
        /// </summary>
        [Fact]
        [Trait("Category", "Build")]
        public void A_real_build_writes_the_lock_and_warns_about_a_rename()
        {
            var dir = Path.Combine(Path.GetTempPath(), "betta-lock-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var build = Path.Combine(RepoRoot(), "Betta.Generators", "build");
                var bin = AppContext.BaseDirectory;
                File.WriteAllText(Path.Combine(dir, "LockProbe.csproj"), $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>11</LangVersion>
  </PropertyGroup>
  <Import Project=""{build}\Betta.Generators.props"" />
  <ItemGroup>
    <Analyzer Include=""{bin}Betta.Generators.dll"" />
    <Reference Include=""{bin}Betta.Abstractions.dll"" />
  </ItemGroup>
  <Import Project=""{build}\Betta.Generators.targets"" />
</Project>");
                var source = Path.Combine(dir, "Pack.cs");
                var lockFile = Path.Combine(dir, "betta.lock");

                File.WriteAllText(source, Pack(Remap));
                var first = DotnetBuild(dir);
                Assert.True(first.ExitCode == 0, first.Output);
                var locked = File.ReadAllText(lockFile);
                Assert.StartsWith("# betta.lock:", locked);
                Assert.Contains(" LockProbe.IMathPack.Remap", locked);

                File.WriteAllText(source, Pack(@"[GrasshopperMethod(""Remap"")] double Map(double value);"));
                var renamed = DotnetBuild(dir);
                Assert.True(renamed.ExitCode == 0, renamed.Output);
                Assert.Contains("warning BETTA012", renamed.Output);
                Assert.Equal(locked, File.ReadAllText(lockFile));

                File.WriteAllText(source, Pack(@"[GrasshopperMethod(""Remap"", FormerNames = new[] { ""Remap"" })] double Map(double value);"));
                var kept = DotnetBuild(dir);
                Assert.True(kept.ExitCode == 0, kept.Output);
                Assert.DoesNotContain("BETTA012", kept.Output);
                var after = File.ReadAllText(lockFile);
                Assert.StartsWith(locked, after);
                Assert.Contains(" LockProbe.IMathPack.Map", after);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static (int ExitCode, string Output) DotnetBuild(string dir)
        {
            var psi = new ProcessStartInfo("dotnet", "build -nologo -v:m -nodeReuse:false -p:UseSharedCompilation=false")
            {
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
            psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            // Set by the dotnet that runs these tests; the nested build resolves its own.
            foreach (var name in new[] { "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildExtensionsPath" })
                psi.Environment.Remove(name);

            using var process = Process.Start(psi);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10 * 60 * 1000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("dotnet build did not finish in 10 minutes");
            }
            return (process.ExitCode, stdout.Result + stderr.Result);
        }

        private static string RepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Betta.Generators", "build", "Betta.Generators.targets")))
                    return dir.FullName;
            throw new DirectoryNotFoundException("Betta.Generators/build not found above " + AppContext.BaseDirectory);
        }

        internal sealed class Text : AdditionalText
        {
            private readonly string _text;
            public Text(string path, string text) { Path = path; _text = text; }
            public override string Path { get; }
            public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(_text);
        }

        private sealed class Options : AnalyzerConfigOptionsProvider
        {
            private readonly Values _values;
            public Options(string bettaLock) => _values = new Values(bettaLock);
            public override AnalyzerConfigOptions GlobalOptions => _values;
            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _values;
            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _values;

            private sealed class Values : AnalyzerConfigOptions
            {
                private readonly string _bettaLock;
                public Values(string bettaLock) => _bettaLock = bettaLock;
                public override bool TryGetValue(string key, out string value)
                {
                    value = key == "build_property.BettaLock" ? _bettaLock : null;
                    return value != null;
                }
            }
        }
    }
}
