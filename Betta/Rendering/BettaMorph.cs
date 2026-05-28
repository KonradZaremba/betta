// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Drawing;

namespace Betta.Rendering
{
    /// <summary>
    /// A betta color morph — three coordinated colors (body, fins, accent)
    /// inspired by real betta fish coloration. Same silhouette across every
    /// generated component, but the morph supplies the individuality.
    /// </summary>
    public sealed record BettaMorph(string Name, Color Body, Color Fins, Color Accent);
}
