// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Betta.Attributes
{
    /// <summary>
    /// Marks a method parameter as <b>per-instance state controlled by the
    /// component's right-click menu</b>, NOT as a wired input. The user picks
    /// the value once from the menu; it persists with the .gh file via the
    /// component's Read/Write.
    ///
    /// v0.5 supports enum and bool typed parameters out of the box. For other
    /// types, the runtime falls back to "use the [GrasshopperParameter]
    /// DefaultValue and don't surface a UI" — useful as a passthrough until a
    /// richer menu editor lands.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class GrasshopperMenuStateAttribute : Attribute
    {
        /// <summary>
        /// Display name in the right-click menu. Defaults to the CLR parameter
        /// name (or its <see cref="GrasshopperParameterAttribute.Name"/>) when
        /// not specified.
        /// </summary>
        public string Name { get; set; }
    }
}
