// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Drawing;
using System.Reflection;
using Betta.Services;
using Grasshopper.Kernel;

namespace Betta.Components
{
    /// <summary>
    /// One proxy per attributed service method. Gives Grasshopper a distinct
    /// identity (name, GUID, category) in the toolbar while every proxy shares
    /// one runtime component class (BettaComponent).
    ///
    /// Rhino 8 dropped the GH_ObjectProxy base class; we implement
    /// IGH_ObjectProxy directly.
    /// </summary>
    public sealed class BettaComponentProxy : IGH_ObjectProxy
    {
        private readonly ComponentDescriptor _descriptor;
        private readonly GH_InstanceDescription _desc;

        public BettaComponentProxy(ComponentDescriptor descriptor)
        {
            _descriptor = descriptor;
            _desc = new GH_InstanceDescription(
                descriptor.Name,
                descriptor.NickName,
                descriptor.Description,
                descriptor.Category,
                descriptor.SubCategory);
            Location = typeof(BettaComponentProxy).Assembly.Location;
            LibraryGuid = typeof(BettaComponentProxy).Assembly.ManifestModule.ModuleVersionId;
        }

        public IGH_InstanceDescription Desc => _desc;
        public Bitmap Icon => IconProvider.GetOrCreate(_descriptor);
        public GH_Exposure Exposure { get; set; } = GH_Exposure.primary;
        public GH_ObjectType Kind => GH_ObjectType.CompiledObject;
        public Guid Guid => _descriptor.Guid;
        public Type Type => typeof(BettaComponent);
        public bool SDKCompliant => true;
        public bool Obsolete => false;
        public string Location { get; set; }
        public Guid LibraryGuid { get; set; }

        public IGH_ObjectProxy DuplicateProxy() => new BettaComponentProxy(_descriptor);

        public IGH_DocumentObject CreateInstance()
        {
            var previous = BettaComponent.Pending;
            BettaComponent.Pending = _descriptor;
            try { return new BettaComponent(); }
            finally { BettaComponent.Pending = previous; }
        }
    }
}
