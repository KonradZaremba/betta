// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Betta
{
    public class BettaInfo : GH_AssemblyInfo
    {
        public override string Name => "Betta";

        public override Bitmap Icon => null;

        public override string Description =>
            "Same silhouette. Every fish is its own. Runtime adaptation for Grasshopper.";

        public override Guid Id => new Guid("24659d98-fdd3-43ce-959d-ddbbfd75c5cb");

        public override string AuthorName => "Konrad Zaremba";

        public override string AuthorContact => "konradzaremba@gmail.com";
    }
}
