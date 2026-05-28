// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Betta.Attributes;
using Betta.Interfaces;

namespace Betta.Strings
{
    [GrasshopperCollection("Strings", "Text")]
    public interface IStringCollection : IBettaCollection
    {
        [GrasshopperMethod("Upper")]
        string ToUpper([GrasshopperParameter("Text")] string text);

        [GrasshopperMethod("Lower")]
        string ToLower([GrasshopperParameter("Text")] string text);

        [GrasshopperMethod("Reverse")]
        string Reverse([GrasshopperParameter("Text")] string text);

        [GrasshopperMethod("Length")]
        int Length([GrasshopperParameter("Text")] string text);

        // Custom-icon demo: ships its own PNG (the detailed painted Forest
        // betta) rather than picking from the default Mini fish rotation.
        // IconProvider resolves IconResource by suffix-matching against the
        // plugin assembly's embedded resource names — see Betta.Strings.csproj.
        [GrasshopperMethod("Concat", IconResource = "concat_fish.png")]
        string Concat(
            [GrasshopperParameter("Left")] string left,
            [GrasshopperParameter("Right")] string right,
            [GrasshopperParameter("Separator")] string separator);
    }
}
