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
    /// Headless coverage for the plugin-trust policy load/save/normalize logic
    /// and the non-signing short-circuits of the verifier. The actual
    /// Authenticode branch (X509 chain build against a signed DLL) needs a real
    /// signed assembly and is deliberately left to fixture/manual testing.
    /// GH-free, file+JSON only — uses temp files, never the real trust.json.
    /// </summary>
    public class TestPluginTrustPolicy : IDisposable
    {
        private readonly string _dir;

        public TestPluginTrustPolicy()
        {
            _dir = Path.Combine(Path.GetTempPath(), "BettaTrustTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private string Path_(string name) => Path.Combine(_dir, name);

        // --- LoadOrOff ------------------------------------------------------

        [Fact]
        public void LoadOrOff_MissingFile_ReturnsOff()
        {
            var policy = PluginTrustPolicy.LoadOrOff(Path_("does-not-exist.json"));

            Assert.Equal(PluginTrustMode.Off, policy.Mode);
            Assert.Empty(policy.AllowedThumbprints);
        }

        [Fact]
        public void LoadOrOff_MalformedJson_FallsBackToOff()
        {
            var path = Path_("garbage.json");
            File.WriteAllText(path, "{ this is not valid json ][");

            var policy = PluginTrustPolicy.LoadOrOff(path);

            Assert.Equal(PluginTrustMode.Off, policy.Mode);
        }

        [Fact]
        public void SaveThenLoad_RoundTripsModeAndThumbprints()
        {
            var path = Path_("trust.json");
            var original = new PluginTrustPolicy
            {
                Mode = PluginTrustMode.Enforce,
            };
            original.AllowedThumbprints.Add("AABBCCDD");
            original.Save(path);

            var loaded = PluginTrustPolicy.LoadOrOff(path);

            Assert.Equal(PluginTrustMode.Enforce, loaded.Mode);
            Assert.Contains("AABBCCDD", loaded.AllowedThumbprints);
        }

        [Fact]
        public void LoadOrOff_NormalizesThumbprints_StripsSeparatorsAndUppercases()
        {
            var path = Path_("messy.json");
            // Hand-written so the raw thumbprints carry spaces/colons/lowercase.
            // Mode 2 == Enforce (Off=0, WarnOnly=1, Enforce=2).
            File.WriteAllText(path,
                "{ \"Mode\": 2, \"AllowedThumbprints\": [ \"ab cd:ef\", \"  12 34  \" ] }");

            var policy = PluginTrustPolicy.LoadOrOff(path);

            Assert.Equal(PluginTrustMode.Enforce, policy.Mode);
            Assert.Contains("ABCDEF", policy.AllowedThumbprints);
            Assert.Contains("1234", policy.AllowedThumbprints);
        }

        [Fact]
        public void Off_FactoryIsOffWithNoPublishers()
        {
            var policy = PluginTrustPolicy.Off();

            Assert.Equal(PluginTrustMode.Off, policy.Mode);
            Assert.Empty(policy.AllowedThumbprints);
        }

        // --- Verifier short-circuits (no signing involved) ------------------

        [Fact]
        public void Verify_NullPolicy_IsTrusted()
        {
            var verdict = PluginTrustVerifier.Verify(Path_("whatever.dll"), null);
            Assert.True(verdict.Trusted);
        }

        [Fact]
        public void Verify_OffPolicy_IsTrusted_WithoutReadingFile()
        {
            // Off short-circuits before any file access, so a non-existent path
            // must still come back trusted.
            var verdict = PluginTrustVerifier.Verify(Path_("no-such.dll"), PluginTrustPolicy.Off());

            Assert.True(verdict.Trusted);
            Assert.Equal("signing disabled", verdict.Reason);
        }

        [Fact]
        public void Verify_EnforceButFileMissing_IsNotTrusted()
        {
            var policy = new PluginTrustPolicy { Mode = PluginTrustMode.Enforce };

            var verdict = PluginTrustVerifier.Verify(Path_("no-such.dll"), policy);

            Assert.False(verdict.Trusted);
            Assert.Equal("file not found", verdict.Reason);
        }
    }
}
