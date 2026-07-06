// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Betta.Attributes
{
    /// <summary>
    /// Attaches a preset dropdown of values to a wired input parameter. Where
    /// <c>[GrasshopperMenuState]</c> covers per-instance enum/bool state that
    /// lives in the component's right-click menu, this attribute seeds a
    /// standard GH <c>Param_Integer</c> / <c>Param_String</c> / <c>Param_Number</c>
    /// with a persistent list of options users can pick from via the input's
    /// own context menu — the "value list dropdown" ergonomic that hand-written
    /// GH components achieve by dropping a <c>GH_ValueList</c> onto the canvas.
    ///
    /// Two shapes:
    /// - <c>[GrasshopperValueList("1:1", "16:9", "4:3")]</c> — a list of items
    ///   used both as label and value.
    /// - <c>[GrasshopperValueList(Items = new[] { "1:1", "16:9" })]</c> — same,
    ///   named form for readability.
    ///
    /// Item strings are parsed with the same rules a user typing into a GH
    /// panel would trigger: integers/doubles into their numeric params, raw
    /// strings into <c>Param_String</c>. For enum-typed parameters, prefer
    /// <c>[GrasshopperMenuState]</c> (menu-state) instead — that gives an
    /// exclusive right-click dropdown without leaking a wire.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class GrasshopperValueListAttribute : Attribute
    {
        /// <summary>Preset items. Both label and value.</summary>
        public string[] Items { get; set; }

        public GrasshopperValueListAttribute() { }
        public GrasshopperValueListAttribute(params string[] items) { Items = items; }
    }
}
