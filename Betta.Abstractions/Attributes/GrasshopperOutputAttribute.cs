// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Betta.Attributes
{
    /// <summary>
    /// Customizes how method outputs are exposed in the generated Grasshopper component
    /// </summary>
    [AttributeUsage(AttributeTargets.ReturnValue | AttributeTargets.Method, AllowMultiple = true)]
    public class GrasshopperOutputAttribute : Attribute
    {
        /// <summary>
        /// Display name of the output
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Short nickname for the output
        /// </summary>
        public string NickName { get; set; }

        /// <summary>
        /// Description of the output
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Index of the output (for multiple outputs)
        /// </summary>
        public int Index { get; set; }

        public GrasshopperOutputAttribute()
        {
        }

        public GrasshopperOutputAttribute(string name, string description = null)
        {
            Name = name;
            Description = description;
        }

        public GrasshopperOutputAttribute(string name, string nickName, string description, int index = 0)
        {
            Name = name;
            NickName = nickName;
            Description = description;
            Index = index;
        }
    }
}