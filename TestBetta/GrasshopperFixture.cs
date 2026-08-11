// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace Rhino.Test
{
  public class GHFileFixture : IDisposable
  {
    private object _Doc { get; set; }
    public GrasshopperSingleton GH => GrasshopperSingleton.Instance;
    public string FilePath { get; private set; }
    private bool _isDisposed = false;

    static GHFileFixture()
    {
      // This MUST be included in a static constructor to ensure that no Rhino DLLs
      // are loaded before the resolver is set up. Avoid creating other static functions
      // and members which may reference Rhino assemblies, as that may cause those
      // assemblies to be loaded before this is called.
      GrasshopperSingleton.InitializeResolver();
    }
    public GHFileFixture(string filePath)
    {
      FilePath = filePath;
      GrasshopperSingleton.Instance.InitializeCore();
    }
    public Grasshopper.Kernel.GH_Document Doc
    {
      get
      {
        return LoadGrasshopperDoc(FilePath);
      }
    }

    public Grasshopper.Kernel.GH_Document LoadGrasshopperDoc(string filePath)
    {
      if (filePath != FilePath)
      {
        GH.GHPlugin.CloseDocument();
        _Doc = null;
        FilePath = null;
      }
      if (null != _Doc)
        return _Doc as Grasshopper.Kernel.GH_Document;
      if (!GH.DocIO.Open(filePath))
        throw new InvalidOperationException("File Loading Failed");
      else
      {
        var doc = GH.DocIO.Document;
        _Doc = doc;

        // Documents are typically only enabled when they are loaded
        // into the Grasshopper canvas. In this case we -may- want to
        // make sure our document is enabled before using it.
        doc.Enabled = true;
      }
      return Doc;
    }
    protected virtual void Dispose(bool disposing)
    {
      if (_isDisposed) return;
      if (disposing)
      {
        GH.GHPlugin.CloseDocument();
        _Doc = null;
        FilePath = null;
      }

      // TODO: free unmanaged resources (unmanaged objects) and override finalizer
      // TODO: set large fields to null
      _isDisposed = true;
    }

    public void Dispose()
    {
      // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
      Dispose(disposing: true);
      GC.SuppressFinalize(this);
    }
  }

  public class GrasshopperSingleton : IDisposable
  {
    private static bool _isResolverInitialized = false;
    private static GrasshopperSingleton _instance = null;
    public static GrasshopperSingleton Instance
    {
      get
      {
        if (null == _instance)
          _instance = new GrasshopperSingleton();
        return _instance;
      }
    }
    static GrasshopperSingleton()
    {
      // This MUST be included in a static constructor to ensure that no Rhino DLLs
      // are loaded before the resolver is set up. Avoid creating other static functions
      // and members which may reference Rhino assemblies, as that may cause those
      // assemblies to be loaded before this is called.
      InitializeResolver();
    }

    /// <summary>
    /// THE single entry point for assembly resolution: after this returns, both
    /// RhinoCommon (via RhinoInside.Resolver, from &lt;Rhino&gt;\System) and
    /// Grasshopper/GH_IO (via the Plug-ins probe below — the resolver does not
    /// cover them) are resolvable. Idempotent; safe from any call site.
    /// </summary>
    public static void InitializeResolver()
    {
      if (_isResolverInitialized) return;
      RhinoInside.Resolver.Initialize();
      AppDomain.CurrentDomain.AssemblyResolve += ResolveFromRhinoPlugins;
      _isResolverInitialized = true;
    }

    private static string _rhinoPluginsGrasshopperDir;

    // RhinoInside.Resolver only covers Rhino's System folder; Grasshopper.dll /
    // GH_IO.dll live in Plug-ins\Grasshopper, so tests touching a GH type
    // without a running Rhino (Param_BettaGoo<T>, say) need this probe. The
    // directory follows whichever Rhino the resolver picked; never hardcoded.
    private static Assembly ResolveFromRhinoPlugins(object sender, ResolveEventArgs args)
    {
      var name = new AssemblyName(args.Name).Name;
      // Satellite-assembly probes fire often and can never live there.
      if (name == null || name.EndsWith(".resources", StringComparison.Ordinal)) return null;

      if (_rhinoPluginsGrasshopperDir == null)
      {
        var system = RhinoInside.Resolver.RhinoSystemDirectory;
        if (string.IsNullOrEmpty(system)) return null;
        var root = Path.GetDirectoryName(
          system.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (root == null) return null;
        _rhinoPluginsGrasshopperDir = Path.Combine(root, "Plug-ins", "Grasshopper");
      }

      var candidate = Path.Combine(_rhinoPluginsGrasshopperDir, name + ".dll");
      return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }
    private object _Core = null;
    private object _GHPlugin = null;
    private bool _isDisposed = false;
    private object _docIO { get; set; }
    public GrasshopperSingleton()
    {
      InitializeCore();
    }
    public Rhino.Runtime.InProcess.RhinoCore Core
    {
      get
      {
        if (null == _Core) InitializeCore();
        return _Core as Rhino.Runtime.InProcess.RhinoCore;
      }
    }
    public Grasshopper.Plugin.GH_RhinoScriptInterface GHPlugin
    {
      get
      {
        if (null == _GHPlugin) InitializeGrasshopperPlugin();
        return _GHPlugin as Grasshopper.Plugin.GH_RhinoScriptInterface;
      }
    }
    public Grasshopper.Kernel.GH_DocumentIO DocIO
    {
      get
      {
        if (null == _docIO) InitializeDocIO();
        return _docIO as Grasshopper.Kernel.GH_DocumentIO;
      }
    }

    public void InitializeCore()
    {
      if (null == _Core)
      {
        _Core = new Rhino.Runtime.InProcess.RhinoCore();
      }
    }
    void InitializeGrasshopperPlugin()
    {
      if (null == _Core) InitializeCore();
      // we do this in a seperate function to absolutely ensure that the core is initialized before we load the GH plugin,
      // which will happen automatically when we enter the function containing GH references
      InitializeGrasshopperPlugin2();
    }
    void InitializeGrasshopperPlugin2()
    {
      _GHPlugin = Rhino.RhinoApp.GetPlugInObject("Grasshopper");
      var ghp = _GHPlugin as Grasshopper.Plugin.GH_RhinoScriptInterface;
      ghp.RunHeadless();
    }
    void InitializeDocIO()
    {
      // we do this in a seperate function to absolutely ensure that the core is initialized before we load the GH plugin,
      // which will happen automatically when we enter the function containing GH references
      if (null == _GHPlugin) InitializeGrasshopperPlugin();
      InitializeDocIO2();
    }
    void InitializeDocIO2()
    {
      var docIO = new Grasshopper.Kernel.GH_DocumentIO();
      _docIO = docIO;
    }
    protected virtual void Dispose(bool disposing)
    {
      if (_isDisposed) return;
      if (disposing)
      {
        _docIO = null;
        GHPlugin.CloseAllDocuments();
        _GHPlugin = null;
        Core.Dispose();
      }

      // TODO: free unmanaged resources (unmanaged objects) and override finalizer
      // TODO: set large fields to null
      _isDisposed = true;
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~GrasshopperFixture()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
      // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
      Dispose(disposing: true);
      GC.SuppressFinalize(this);
    }
  }
}


