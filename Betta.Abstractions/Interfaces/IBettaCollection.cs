// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Betta.Interfaces
{
    /// <summary>
    /// Marker interface. Any service interface (or class) that inherits this
    /// is treated as a Betta plugin collection: methods decorated with
    /// [GrasshopperMethod] on the type are published as Grasshopper components
    /// during startup.
    ///
    /// External plugins opt in by referencing Betta.dll, making their
    /// service interface inherit IBettaCollection, and dropping their
    /// compiled DLL into %AppData%/Grasshopper/Libraries/Betta/.
    /// </summary>
    public interface IBettaCollection { }
}
