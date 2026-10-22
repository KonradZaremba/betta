// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Betta.Attributes
{
    /// <summary>
    /// Marks a service method as eligible for Grasshopper component generation
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class GrasshopperMethodAttribute : Attribute
    {
        /// <summary>
        /// Display name of the component
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Short nickname for the component
        /// </summary>
        public string NickName { get; set; }

        /// <summary>
        /// Description of what the component does
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Category for component organization
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Subcategory for component organization
        /// </summary>
        public string SubCategory { get; set; }

        /// <summary>
        /// Optional explicit GUID, e.g. the <c>ComponentGuid</c> of a ported GH_Component.
        /// If not provided, it is derived from the type and method name.
        /// </summary>
        public string Guid { get; set; }

        /// <summary>
        /// Earlier names of this method, so .gh files saved before a rename still open:
        /// <c>FormerNames = new[] { "Remap" }</c>. For a method moved from another type,
        /// give that type's full name with it: <c>"MyPlugin.IMathPack.Remap"</c>. Files
        /// open through a hidden alias and take the current GUID when saved again.
        /// Adding an overload (a second method with the same name) also changes the GUID
        /// of the one that was there first; list its own name on it to keep its files.
        /// </summary>
        public string[] FormerNames { get; set; }

        /// <summary>
        /// Whether this method should be exposed as a component (default: true)
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Optional embedded resource name for the 24x24 toolbar icon.
        /// Example: "Betta.Resources.my_icon.png". When null/missing,
        /// IconProvider generates a default from the NickName + category.
        /// </summary>
        public string IconResource { get; set; }

        public GrasshopperMethodAttribute()
        {
        }

        public GrasshopperMethodAttribute(string name, string description = null)
        {
            Name = name;
            Description = description;
        }
    }
}