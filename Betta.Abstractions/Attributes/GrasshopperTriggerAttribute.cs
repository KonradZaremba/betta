// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Betta.Attributes
{
    /// <summary>
    /// Marks a bool parameter as a <b>manual-run trigger</b>. The parameter
    /// does not appear as a wired GH input; instead the component surfaces a
    /// "Run" menu item and does not solve until the user clicks it. On click,
    /// the parameter is injected as <c>true</c> for exactly one solve; every
    /// subsequent solve (triggered by upstream wire changes) sees the trigger
    /// as <c>false</c> and skips the method body until "Run" is clicked again.
    ///
    /// The pattern the ecosystem was faking before: an expensive async method
    /// (a cloud-API call) exposed a <c>bool run</c> input and asked users to
    /// wire a toggle. With <c>[GrasshopperTrigger]</c> it's built in — no
    /// stray wires, no accidental fires, and the "Run" menu item is the only
    /// UI users have to learn.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class GrasshopperTriggerAttribute : Attribute
    {
        /// <summary>
        /// Display text on the menu item. Defaults to "Run" when unset.
        /// </summary>
        public string Label { get; set; }
    }
}
