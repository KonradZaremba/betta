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
    // Where PluginLoader finds a plugin's dependency. Plugins reference Betta.Preview with
    // ExcludeAssets="runtime", so on a clean install the only copy is the one shipped next
    // to Betta.gha, not in the plugin folder.
    public class TestPluginDependencies : IDisposable
    {
        private readonly string _plugins = Directory.CreateTempSubdirectory("betta-plugins-").FullName;
        private readonly string _runtime = Directory.CreateTempSubdirectory("betta-runtime-").FullName;

        public void Dispose()
        {
            Directory.Delete(_plugins, true);
            Directory.Delete(_runtime, true);
        }

        private static string Touch(string folder, string name)
        {
            var path = Path.Combine(folder, name + ".dll");
            File.WriteAllBytes(path, Array.Empty<byte>());
            return path;
        }

        [Fact]
        public void A_copy_in_the_plugin_folder_wins()
        {
            var own = Touch(_plugins, "Betta.Preview");
            Touch(_runtime, "Betta.Preview");

            Assert.Equal(own, PluginLoader.FindDependency("Betta.Preview", _plugins, _runtime));
        }

        [Fact]
        public void Betta_Preview_is_found_next_to_Betta_gha()
        {
            var shipped = Touch(_runtime, "Betta.Preview");

            Assert.Equal(shipped, PluginLoader.FindDependency("Betta.Preview", _plugins, _runtime));
        }

        [Fact]
        public void Other_assemblies_next_to_Betta_gha_are_not_handed_out()
        {
            Touch(_runtime, "Microsoft.Extensions.Logging");

            Assert.Null(PluginLoader.FindDependency("Microsoft.Extensions.Logging", _plugins, _runtime));
        }

        [Fact]
        public void No_runtime_folder_when_Betta_gha_was_loaded_from_memory()
        {
            Assert.Null(PluginLoader.FindDependency("Betta.Preview", _plugins, null));
        }
    }
}
