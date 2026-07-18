// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Rhino.Test;

namespace TestBetta
{
  internal static class RhinoResolverInit
  {
    // RhinoCommon and Grasshopper are compile-only references — they are never
    // copied to the test output and must be resolved from the installed Rhino by
    // RhinoInside.Resolver. Previously only GHFileFixture's static constructor did
    // that, so any test touching a Grasshopper type without going through the
    // fixture (TestParamRegistry, TestGeneratorBootstrap) died with
    // FileNotFoundException on RhinoCommon.
    //
    // A module initializer runs once at assembly load, before any test body, which
    // satisfies the resolver's hard requirement that no Rhino assembly is loaded
    // before it is installed. This only wires up assembly resolution; it does not
    // start RhinoCore, so tests that need no running Rhino stay headless-safe.
    [ModuleInitializer]
    internal static void Init()
    {
      // NOTE — deliberately NOT setting Resolver.UseLatest = true here.
      //
      // Rhino.Inside 7.0.0 defaults UseLatest=false, so with both Rhino 7 and
      // Rhino 8 installed it resolves to Rhino 7 even though everything here is
      // compiled against RhinoCommon/Grasshopper 8.x. That mismatch is what makes
      // RhinoCore..ctor() fail with COM E_FAIL in the fixture-based tests, and it
      // means Grasshopper.dll below is loaded from Rhino 7 (tolerated — .NET Core
      // does not enforce strict version binding, and the API surface used here is
      // compatible).
      //
      // Setting UseLatest=true does resolve Rhino 8 and RhinoCore then really
      // starts — but the in-process Rhino subsequently crashes the test host,
      // which aborts the ENTIRE run (only ~32 of 101 tests report). A fast, clean
      // E_FAIL that fails 6 tests is strictly better than an aborted suite, so the
      // default stands until Rhino.Inside 8.x exists. See CLAUDE.md.
      GrasshopperSingleton.InitializeResolver();
      AppDomain.CurrentDomain.AssemblyResolve += ResolveFromRhinoPlugins;
    }

    // RhinoInside.Resolver only covers Rhino's System folder (RhinoCommon and
    // friends). Grasshopper.dll / GH_IO.dll live one level over in
    // Plug-ins\Grasshopper, so a test touching a GH type without a running Rhino
    // — constructing Param_BettaGoo<T>, say — cannot resolve them.
    //
    // The folder is derived from Resolver.RhinoSystemDirectory (populated by
    // InitializeResolver above) rather than hardcoded, so this follows whichever
    // Rhino the resolver picked.
    private static Assembly ResolveFromRhinoPlugins(object sender, ResolveEventArgs args)
    {
      var name = new AssemblyName(args.Name).Name;

      var system = RhinoInside.Resolver.RhinoSystemDirectory;
      if (string.IsNullOrEmpty(system)) return null;

      var root = Path.GetDirectoryName(
        system.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
      if (root == null) return null;

      var candidate = Path.Combine(root, "Plug-ins", "Grasshopper", name + ".dll");
      return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }
  }
}
