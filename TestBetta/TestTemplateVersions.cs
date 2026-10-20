// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace TestBetta
{
    /// <summary>
    /// The dotnet-new templates pin the Betta package versions they reference through
    /// template.json defaults. If those drift from Directory.Build.props, a freshly
    /// scaffolded plugin references a version that was never published and fails to
    /// restore (they sat at 0.4.0 through 0.7.x). Bump them with every Version change.
    /// </summary>
    public class TestTemplateVersions
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }

        [Fact]
        public void TemplateVersionDefaults_MatchRepoVersion()
        {
            var root = RepoRoot();
            var version = XDocument.Load(Path.Combine(root, "Directory.Build.props"))
                .Descendants("Version").Single().Value.Trim();

            var files = Directory.GetFiles(Path.Combine(root, "templates", "Betta.Templates", "content"),
                                           "template.json", SearchOption.AllDirectories);
            Assert.NotEmpty(files);

            var mismatches = new List<string>();
            foreach (var file in files)
            {
                using var json = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var symbol in json.RootElement.GetProperty("symbols").EnumerateObject())
                {
                    if (!symbol.Name.StartsWith("Betta") || !symbol.Name.EndsWith("Version")) continue;
                    var value = symbol.Value.GetProperty("defaultValue").GetString();
                    if (value != version)
                        mismatches.Add($"{Path.GetRelativePath(root, file)}: {symbol.Name} = {value}");
                }
            }

            Assert.True(mismatches.Count == 0,
                $"Template defaults must equal <Version> {version} from Directory.Build.props:\n" +
                string.Join("\n", mismatches));
        }
    }
}
