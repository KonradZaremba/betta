// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Betta.Services
{
    /// <summary>
    /// Describes a component that should be generated from a service method
    /// </summary>
    public class ComponentDescriptor
    {
        public Type ServiceType { get; set; }
        public MethodInfo Method { get; set; }
        public string Name { get; set; }
        public string NickName { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string SubCategory { get; set; }
        public Guid Guid { get; set; }
        public bool Enabled { get; set; } = true;
        public string IconResource { get; set; }
        internal System.Drawing.Bitmap CachedIcon { get; set; }

        /// <summary>
        /// True if the method's return type (or list element / tuple element)
        /// implements <c>Betta.Preview.IBettaPreview</c>. Set during
        /// discovery — when true, the BettaComponent runtime caches its last
        /// outputs and forwards IGH_PreviewData calls to them. Detected by
        /// interface name string so the Betta runtime stays free of a hard
        /// reference to the Betta.Preview package.
        /// </summary>
        public bool HasPreview { get; set; }

        /// <summary>
        /// True if the method's return type (or list element / tuple element)
        /// implements <c>Betta.Preview.IBettaBakeable</c>. When true, the
        /// component implements IGH_BakeAwareObject and forwards right-click
        /// → Bake to the cached values.
        /// </summary>
        public bool HasBakeable { get; set; }

        /// <summary>
        /// Entitlement key required to run this component, or null if none.
        /// Populated from <c>[GrasshopperRequiresEntitlement]</c> on the
        /// method (or its declaring class). Enforced at solve time by
        /// consulting the DI-registered <c>IBettaLicenseGate</c>; null means
        /// no entitlement check runs.
        /// </summary>
        public string RequiredEntitlement { get; set; }

        /// <summary>
        /// True if the method has one or more <c>[GrasshopperTrigger]</c>
        /// parameters. The BettaComponent surfaces a "Run" menu item and
        /// skips its solve until the user clicks it. When false, the
        /// component runs on every input change as usual.
        /// </summary>
        public bool HasTrigger { get; set; }

        /// <summary>
        /// Deterministic GUID from service type + method signature. Stable across
        /// .NET Framework and .NET 7; unlike string.GetHashCode() which is not.
        /// Signature (not cosmetic attributes) is hashed so renaming Name/Category
        /// does not invalidate existing .gh documents.
        /// </summary>`
        public static Guid GenerateGuidFromMethod(Type serviceType, MethodInfo method)
        {
            var paramSig = string.Join(",",
                method.GetParameters().Select(p => p.ParameterType.FullName + " " + p.Name));
            var input = $"{serviceType.FullName}|{method.Name}|{paramSig}|{method.ReturnType.FullName}";

            using var md5 = MD5.Create();
            var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            return new Guid(bytes);
        }

        public override string ToString()
        {
            return $"{ServiceType.Name}.{Method.Name} -> {Name}";
        }
    }
}
