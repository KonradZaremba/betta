// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Reflection;
using Betta.Interfaces;

namespace Betta.Attributes
{
    /// <summary>
    /// GH-free reflection helpers that decide whether a type or a method's
    /// return value is opaque (either via [GrasshopperOpaque] on the type /
    /// interface / method / return, or by implementing IBettaValue). Lives in
    /// Abstractions so OutputPlanner and ComponentRegistry can share the same
    /// rule without pulling in Grasshopper types.
    /// </summary>
    public static class OpaqueDetection
    {
        public static bool IsTypeOpaque(Type t)
        {
            if (t == null) return false;
            if (typeof(IBettaValue).IsAssignableFrom(t)) return true;
            if (t.GetCustomAttribute<GrasshopperOpaqueAttribute>() != null) return true;
            // Also honor [GrasshopperOpaque] on an interface the type implements,
            // so a domain type whose interface (not the class) carries the
            // attribute still opts in.
            foreach (var iface in t.GetInterfaces())
                if (iface.GetCustomAttribute<GrasshopperOpaqueAttribute>() != null) return true;
            return false;
        }

        public static bool IsMethodReturnOpaque(MethodInfo m)
        {
            if (m == null) return false;
            if (m.GetCustomAttribute<GrasshopperOpaqueAttribute>() != null) return true;
            if (m.ReturnParameter != null &&
                m.ReturnParameter.GetCustomAttribute<GrasshopperOpaqueAttribute>() != null)
                return true;
            return false;
        }
    }
}
