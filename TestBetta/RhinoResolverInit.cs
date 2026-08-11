// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;
using Rhino.Test;

namespace TestBetta
{
  internal static class RhinoResolverInit
  {
    // A module initializer runs once at assembly load — before any test body or
    // fixture — which satisfies the resolver's hard requirement that no Rhino
    // assembly is loaded before it is installed. All resolution logic lives in
    // GrasshopperSingleton.InitializeResolver; this file contributes only the
    // timing guarantee.
    //
    // Deliberately NOT setting Resolver.UseLatest = true: resolving Rhino 8
    // makes the in-process RhinoCore start and then crash the test host, which
    // aborts the entire run. A fast clean E_FAIL costing 6 tests beats an
    // aborted suite. Full investigation in CLAUDE.md.
    [ModuleInitializer]
    internal static void Init() => GrasshopperSingleton.InitializeResolver();
  }
}
