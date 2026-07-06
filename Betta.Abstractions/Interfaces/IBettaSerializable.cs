// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Betta.Interfaces
{
    /// <summary>
    /// Opt-in serialization hook for opaque values. By default GH_BettaGoo&lt;T&gt;
    /// does not persist opaque values into the .gh file — pipelines recompute
    /// from wired inputs on reload. For values whose state should survive
    /// (user-edited contents, large pre-computed caches, etc.), implement this
    /// interface and Betta's goo wrapper will round-trip you through
    /// GH_IO chunks.
    ///
    /// Implementations should:
    ///   - <see cref="ToBytes"/> — return a byte[] capturing the state.
    ///   - <see cref="LoadFromBytes"/> — restore from the byte[] (instance
    ///     method, mutates this instance; called on a fresh parameterless ctor).
    ///
    /// The type must therefore expose a public parameterless constructor.
    /// </summary>
    public interface IBettaSerializable
    {
        byte[] ToBytes();
        void LoadFromBytes(byte[] bytes);
    }
}
