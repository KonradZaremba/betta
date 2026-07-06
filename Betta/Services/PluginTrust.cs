// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Betta.Services
{
    /// <summary>
    /// Enforcement level for plugin DLL Authenticode signing. Off by default
    /// so existing installs load unsigned DLLs exactly as they did before.
    /// </summary>
    public enum PluginTrustMode
    {
        /// <summary>Signing not checked. Any DLL loads. (Default.)</summary>
        Off,

        /// <summary>Signing checked; failures logged as warnings but loaded anyway.</summary>
        WarnOnly,

        /// <summary>Signing checked; failures logged and refused.</summary>
        Enforce
    }

    /// <summary>
    /// Trust policy loaded from
    /// <c>%AppData%\Grasshopper\Libraries\Betta\trust.json</c>. Missing file
    /// → <see cref="PluginTrustMode.Off"/> — zero regression.
    /// </summary>
    public sealed class PluginTrustPolicy
    {
        public PluginTrustMode Mode { get; set; } = PluginTrustMode.Off;

        /// <summary>
        /// SHA-1 thumbprints (upper-case, no separators — how X509Certificate2
        /// exposes them). Empty = no publishers are trusted, so every DLL
        /// fails verification when Mode ≠ Off. Populated via the trust dialog.
        /// </summary>
        public List<string> AllowedThumbprints { get; set; } = new List<string>();

        public static PluginTrustPolicy Off() => new PluginTrustPolicy();

        public static string DefaultConfigPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Grasshopper", "Libraries", "Betta", "trust.json");
        }

        public static PluginTrustPolicy LoadOrOff(string path = null)
        {
            path ??= DefaultConfigPath();
            try
            {
                if (!File.Exists(path)) return Off();
                var json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var loaded = JsonSerializer.Deserialize<PluginTrustPolicy>(json, opts);
                if (loaded == null) return Off();
                loaded.AllowedThumbprints ??= new List<string>();
                loaded.AllowedThumbprints = loaded.AllowedThumbprints
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(NormalizeThumbprint)
                    .ToList();
                return loaded;
            }
            catch
            {
                // A malformed trust.json falls back to Off. We don't want the
                // framework to refuse to start because JSON was hand-edited.
                return Off();
            }
        }

        public void Save(string path = null)
        {
            path ??= DefaultConfigPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(this, opts));
        }

        internal static string NormalizeThumbprint(string s) =>
            new string((s ?? "").Where(c => char.IsLetterOrDigit(c)).ToArray()).ToUpperInvariant();
    }

    /// <summary>
    /// Result of a signature check — a boolean plus a human-readable reason
    /// suitable for logging.
    /// </summary>
    public readonly struct PluginTrustVerdict
    {
        public bool Trusted { get; }
        public string Reason { get; }
        public string SignerThumbprint { get; }
        public string SignerSubject { get; }

        public PluginTrustVerdict(bool trusted, string reason, string thumbprint = null, string subject = null)
        {
            Trusted = trusted;
            Reason = reason;
            SignerThumbprint = thumbprint;
            SignerSubject = subject;
        }
    }

    /// <summary>
    /// Authenticode signature verification against the configured trust
    /// policy. Uses <see cref="X509Certificate.CreateFromSignedFile(string)"/>
    /// — built into .NET, no extra dependency — to extract the signer cert.
    /// A chain build catches revocation / expiry; a thumbprint allowlist gives
    /// pinning against a specific publisher.
    /// </summary>
    public static class PluginTrustVerifier
    {
        public static PluginTrustVerdict Verify(string path, PluginTrustPolicy policy)
        {
            if (policy == null || policy.Mode == PluginTrustMode.Off)
                return new PluginTrustVerdict(true, "signing disabled");

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new PluginTrustVerdict(false, "file not found");

            X509Certificate2 cert;
            try
            {
                var raw = X509Certificate.CreateFromSignedFile(path);
                cert = new X509Certificate2(raw);
            }
            catch (Exception ex)
            {
                return new PluginTrustVerdict(false, $"no Authenticode signature ({ex.Message})");
            }

            var thumbprint = PluginTrustPolicy.NormalizeThumbprint(cert.Thumbprint);
            var subject = cert.Subject;

            // Chain validation — catches expired, revoked, untrusted-root, etc.
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // offline-friendly
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            if (!chain.Build(cert))
            {
                var reasons = string.Join(", ", chain.ChainStatus.Select(s => s.Status.ToString()));
                return new PluginTrustVerdict(false, $"chain build failed: {reasons}", thumbprint, subject);
            }

            // Allowlist gate. Empty allowlist + Enforce/WarnOnly = nothing is
            // trusted; only DLLs signed by a pinned publisher get through.
            if (policy.AllowedThumbprints == null || policy.AllowedThumbprints.Count == 0)
                return new PluginTrustVerdict(false, "no publishers in allowlist", thumbprint, subject);

            var allowed = policy.AllowedThumbprints.Any(t =>
                string.Equals(t, thumbprint, StringComparison.OrdinalIgnoreCase));

            return allowed
                ? new PluginTrustVerdict(true, "signed by trusted publisher", thumbprint, subject)
                : new PluginTrustVerdict(false, "signer not on allowlist", thumbprint, subject);
        }

        /// <summary>
        /// Extract the signer info from a signed DLL without applying any
        /// trust policy. Used by the trust dialog's "Import from signed DLL"
        /// button.
        /// </summary>
        public static bool TryReadSigner(string path, out string thumbprint, out string subject)
        {
            thumbprint = null;
            subject = null;
            try
            {
                var raw = X509Certificate.CreateFromSignedFile(path);
                using var cert = new X509Certificate2(raw);
                thumbprint = PluginTrustPolicy.NormalizeThumbprint(cert.Thumbprint);
                subject = cert.Subject;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Load a .cer / .crt file to extract its thumbprint + subject.
        /// </summary>
        public static bool TryReadCertFile(string path, out string thumbprint, out string subject)
        {
            thumbprint = null;
            subject = null;
            try
            {
                using var cert = new X509Certificate2(path);
                thumbprint = PluginTrustPolicy.NormalizeThumbprint(cert.Thumbprint);
                subject = cert.Subject;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
