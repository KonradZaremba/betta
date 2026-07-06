// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using Betta.Components;
using Betta.Services;
using Rhino;
using Rhino.Commands;

namespace Betta.Commands
{
    /// <summary>
    /// Rhino command — typed as <c>Betta_Status</c> at the Rhino command line
    /// — that dumps every Betta-registered descriptor to the command pane.
    /// The per-line format is identical to <see cref="BettaInspector"/>'s
    /// component output, so users can cross-check the two surfaces.
    ///
    /// Registration: Rhino auto-discovers <see cref="Rhino.Commands.Command"/>
    /// subclasses by reflecting over every loaded plugin assembly. Since
    /// Betta.gha is loaded as a Grasshopper plugin (not a true Rhino plugin),
    /// auto-discovery there is not guaranteed across all Rhino versions — if
    /// the command does not appear, the fallback is to add a thin Rhino
    /// PlugIn shim that explicitly calls <c>Rhino.PlugIns.PlugIn.LoadPlugIn</c>
    /// on Betta.gha. On Rhino 8 this is typically not required: the loader
    /// scans Grasshopper assemblies for Command subclasses as well.
    /// Confirmed quirks are tracked at the bottom of this file.
    /// </summary>
    public sealed class BettaStatusCommand : Command
    {
        public override string EnglishName => "Betta_Status";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var descriptors = DescriptorCache.All
                .OrderBy(d => d.Category ?? string.Empty)
                .ThenBy(d => d.SubCategory ?? string.Empty)
                .ThenBy(d => d.Name ?? string.Empty)
                .ToList();

            RhinoApp.WriteLine($"[Betta] {descriptors.Count} descriptor(s) registered:");

            if (descriptors.Count == 0)
            {
                RhinoApp.WriteLine("[Betta]   (none — DescriptorCache is empty)");
                return Result.Success;
            }

            foreach (var d in descriptors)
            {
                // BettaInspector.Format produces the same column-aligned line
                // the component emits, so the two surfaces stay in sync.
                RhinoApp.WriteLine(BettaInspector.Format(d));
            }

            return Result.Success;
        }
    }
}

// -----------------------------------------------------------------------------
// Registration notes (kept in-file because they're discovered quirks, not
// design — when the next person hits this they should find the receipts here):
//
// Rhino auto-discovers Command subclasses by walking every assembly loaded
// into the AppDomain. For "true" Rhino plugins this happens at PlugInLoad;
// for Grasshopper plugins (Betta.gha) Rhino 8 also scans, but only after the
// plugin has been loaded — which Grasshopper does on first GH startup, not
// on Rhino startup. Practical consequence: the FIRST Rhino session after
// install, "Betta_Status" is unknown until the user opens Grasshopper at
// least once. From then on it is registered for the lifetime of the Rhino
// process (and re-resolved on subsequent sessions because Grasshopper loads
// its plugins at Rhino startup once its own RhinoPlugIn initializes).
// -----------------------------------------------------------------------------
