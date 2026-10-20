// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
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
    }
}
