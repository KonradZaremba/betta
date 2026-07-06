// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Betta.Interfaces
{
    /// <summary>
    /// Marker that opts a domain type into "default via parameterless ctor"
    /// behavior. When a Betta method parameter typed as <c>T : IBettaDefault</c>
    /// arrives unwired (null), Betta constructs a fresh <c>T</c> via its
    /// parameterless ctor and hands it to the method body — so authors don't
    /// have to null-check every opaque input.
    ///
    /// The marker exists so the behavior is opt-in: a class without it gets
    /// null on unwired sockets, the same as today.
    /// </summary>
    public interface IBettaDefault { }
}
