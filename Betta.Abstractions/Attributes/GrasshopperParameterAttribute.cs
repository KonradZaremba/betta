// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Betta.Attributes
{
    /// <summary>
    /// Customizes how a method parameter is exposed in the generated Grasshopper component.
    /// Access (item vs. list vs. tree) is not on this attribute: the runtime infers it from
    /// the parameter's CLR type (List&lt;T&gt; → list, otherwise → item).
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class GrasshopperParameterAttribute : Attribute
    {
        public string Name { get; set; }
        public string NickName { get; set; }
        public string Description { get; set; }
        public bool Optional { get; set; }

        /// <summary>
        /// Baked into the GH input's persistent data at registration, so
        /// unwired inputs have a sensible default instead of default(T).
        /// Attribute arguments can only be compile-time constants — so expect
        /// primitives/strings/enums here; complex defaults belong in code.
        /// </summary>
        public object DefaultValue { get; set; }

        public GrasshopperParameterAttribute() { }

        public GrasshopperParameterAttribute(string name, string description = null)
        {
            Name = name;
            Description = description;
        }

        public GrasshopperParameterAttribute(string name, string nickName, string description)
        {
            Name = name;
            NickName = nickName;
            Description = description;
        }
    }
}
