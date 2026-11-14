// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Betta.Services;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// Pins PluginClassifier (testing-brief Tier 1, bug #1): plugin DLLs must classify as
    /// plugins and dependency DLLs as dependencies WITHOUT being loaded, because
    /// byte-loading a dependency into a collectible ALC poisons later binds (0x80131515).
    /// The fixtures are real DLLs the test project copies next to TestBetta.dll.
    /// </summary>
    public class TestPluginClassifier
    {
        private static readonly string Bin = AppContext.BaseDirectory;

        private static bool IsPlugin(string fileName) =>
            PluginClassifier.IsPluginDll(Path.Combine(Bin, fileName), Bin, logger: null);

        [Theory]
        [InlineData("Betta.OpaqueDemo.dll")]
        [InlineData("Betta.Files.dll")]
        public void SamplePlugins_ClassifyAsPlugins(string dll) => Assert.True(IsPlugin(dll));

        [Theory]
        [InlineData("Microsoft.Extensions.DependencyInjection.dll")] // no Betta.Abstractions reference
        [InlineData("Betta.Abstractions.dll")]                        // the contract itself declares no collection
        public void Dependencies_ClassifyAsDependencies(string dll) => Assert.False(IsPlugin(dll));

        [Fact]
        public void UnreadableFile_FailsOpenAsPlugin()
        {
            // Fail-open contract: a classifier error must never make a plugin vanish;
            // the caller falls back to the legacy load path instead.
            Assert.True(IsPlugin("does-not-exist.dll"));
        }

        /// <summary>
        /// A plugin's build once copied its own Betta.Abstractions.dll into the plugin folder, next to the one Betta
        /// uses in the Libraries folder. Same name and version, different MVID: MetadataLoadContext refused the name,
        /// IBettaCollection stopped resolving, and every plugin was skipped as "not a Betta plugin".
        /// </summary>
        [Fact]
        public void StrayAbstractionsCopyInPluginFolder_DoesNotHidePlugins()
        {
            string libraries = Path.Combine(Path.GetTempPath(), "betta-classifier-" + Guid.NewGuid().ToString("N"));
            string plugins = Path.Combine(libraries, "Betta");
            Directory.CreateDirectory(plugins);
            try
            {
                File.Copy(Path.Combine(Bin, "Betta.Abstractions.dll"), Path.Combine(libraries, "Betta.Abstractions.dll"));
                string plugin = Path.Combine(plugins, "Betta.OpaqueDemo.dll");
                File.Copy(Path.Combine(Bin, "Betta.OpaqueDemo.dll"), plugin);
                Assert.True(PluginClassifier.IsPluginDll(plugin, plugins, logger: null));

                byte[] otherBuild = WithNewMvid(File.ReadAllBytes(Path.Combine(Bin, "Betta.Abstractions.dll")));
                File.WriteAllBytes(Path.Combine(plugins, "Betta.Abstractions.dll"), otherBuild);
                Assert.True(PluginClassifier.IsPluginDll(plugin, plugins, logger: null));
            }
            finally
            {
                try { Directory.Delete(libraries, recursive: true); } catch (IOException) { }
            }
        }

        /// <summary>The same assembly with another module version ID, as a separate build of it has.</summary>
        private static byte[] WithNewMvid(byte[] image)
        {
            Guid mvid;
            using (var pe = new PEReader(new MemoryStream(image)))
            {
                MetadataReader md = pe.GetMetadataReader();
                mvid = md.GetGuid(md.GetModuleDefinition().Mvid);
            }
            byte[] old = mvid.ToByteArray();
            byte[] replacement = Guid.NewGuid().ToByteArray();
            int found = 0;
            for (int i = 0; i + 16 <= image.Length; i++)
            {
                if (!image.AsSpan(i, 16).SequenceEqual(old)) continue;
                replacement.CopyTo(image, i);
                found++;
            }
            Assert.True(found > 0, "MVID not found in the image");
            return image;
        }
    }
}
