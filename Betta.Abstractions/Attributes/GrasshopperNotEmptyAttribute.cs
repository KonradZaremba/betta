// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Betta.Attributes
{
    /// <summary>
    /// Non-empty check for a parameter. Strings must not be null or
    /// whitespace; collections must contain at least one element. Triggers a
    /// Warning + skips invocation when violated.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class GrasshopperNotEmptyAttribute : Attribute
    {
    }
}
