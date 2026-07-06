// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Security.Cryptography;
using System.Text;
using Grasshopper.Kernel;

namespace Betta.Goo
{
    /// <summary>
    /// Generic IGH_Param for opaque Betta domain values. Each closed generic
    /// type T gets a stable ComponentGuid derived deterministically from
    /// typeof(T).FullName via MD5 — same input, same Guid, every machine — so
    /// saved .gh files reload across rebuilds and across machines.
    /// </summary>
    public class Param_BettaGoo<T> : GH_Param<GH_BettaGoo<T>> where T : class
    {
        public Param_BettaGoo()
            : base(new GH_InstanceDescription(
                typeof(T).Name,
                typeof(T).Name,
                $"Betta opaque value of type {typeof(T).FullName}",
                "Betta",
                "Goo"))
        {
        }

        public override Guid ComponentGuid => StableGuid;

        // Cache so reflection doesn't run on the hot path. Closed-generic types
        // get their own static field instance, so this is per-T.
        private static readonly Guid StableGuid = ComputeStableGuid(typeof(T));

        private static Guid ComputeStableGuid(Type t)
        {
            using var md5 = MD5.Create();
            var input = $"Betta.Param_BettaGoo|{t.FullName}";
            return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(input)));
        }

        // Hidden from the GH parameter palette: instances are produced by
        // Betta's runtime descriptor pipeline, not dragged onto the canvas
        // standalone.
        public override GH_Exposure Exposure => GH_Exposure.hidden;
    }
}
