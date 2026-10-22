// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Betta.Attributes
{
    /// <summary>
    /// Sets the default Category and SubCategory for every [GrasshopperMethod]
    /// on the decorated type. Any method-level Category/SubCategory wins over
    /// these defaults when specified, so overrides still work.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, AllowMultiple = false)]
    public class GrasshopperCollectionAttribute : Attribute
    {
        public string Category { get; set; }
        public string SubCategory { get; set; }

        /// <summary>
        /// Earlier full names (namespace and type) of this type, so .gh files saved before
        /// it was renamed or moved still find its components:
        /// <c>FormerNames = new[] { "MyPlugin.IMathPack" }</c>. Files open through a hidden
        /// alias and take the current GUID when saved again.
        /// </summary>
        public string[] FormerNames { get; set; }

        public GrasshopperCollectionAttribute() { }

        public GrasshopperCollectionAttribute(string category, string subCategory = "General")
        {
            Category = category;
            SubCategory = subCategory;
        }
    }
}
