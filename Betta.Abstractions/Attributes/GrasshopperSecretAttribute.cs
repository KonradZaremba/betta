// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Betta.Attributes
{
    /// <summary>
    /// Marks a string parameter as a <b>secret pulled from the OS credential
    /// store</b>, not a wired GH input. The Betta runtime does not expose the
    /// parameter as an input pin — instead, at solve time it reads the value
    /// keyed by <see cref="Service"/> from Windows Credential Manager (DPAPI-
    /// backed) and injects it. If no value is set the component reports a
    /// warning and skips that iteration.
    ///
    /// Users manage stored values via the <c>Betta_Secrets</c> Rhino command
    /// or the GH menu → Betta → Secrets… entry.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class GrasshopperSecretAttribute : Attribute
    {
        /// <summary>
        /// Credential-store key. Convention: dot-separated
        /// <c>{provider}.{purpose}</c>, e.g. <c>openai.api_key</c>. Secrets are
        /// per-user, per-service — every component that names the same
        /// <see cref="Service"/> shares the same value.
        /// </summary>
        public string Service { get; }

        /// <summary>
        /// User-facing description of what the secret is used for. Shown in
        /// the "missing secret" warning and in the settings UI.
        /// </summary>
        public string Prompt { get; set; }

        public GrasshopperSecretAttribute(string service)
        {
            Service = service;
        }
    }
}
