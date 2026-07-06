// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Betta.Interfaces
{
    /// <summary>
    /// Custom validator hook for <see cref="Betta.Attributes.GrasshopperValidationAttribute"/>.
    /// Implementations must expose a public parameterless constructor — the
    /// runtime instantiates one per solve.
    /// </summary>
    public interface IBettaValidator
    {
        /// <summary>
        /// Inspect <paramref name="value"/>; return null if it's acceptable,
        /// or a non-null warning message if not. The component then surfaces
        /// the message and skips the invocation rather than letting the
        /// service method see the bad input.
        /// </summary>
        string Validate(object value);
    }
}
