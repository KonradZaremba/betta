// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Betta.Interfaces
{
    /// <summary>
    /// Marker interface. Domain types that implement this signal "treat me as
    /// opaque" — Betta wraps the type in a generated GH_Goo&lt;T&gt; + matching
    /// Param&lt;T&gt; instead of exploding it into one output per public
    /// property. Functionally equivalent to applying [GrasshopperOpaque] on
    /// the type; pick whichever opt-in style your codebase prefers.
    /// </summary>
    public interface IBettaValue { }
}
