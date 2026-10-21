// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Betta.Mcp.Inspection;

/// <summary>
/// Resolves the paths Betta uses on a stock Windows install. Overridable via
/// environment variables so the MCP server is testable on machines that don't
/// have Rhino, and so a user with a custom Grasshopper Libraries location can
/// still wire it up.
/// </summary>
public static class Paths
{
    /// <summary>
    /// The plugin drop folder Betta watches. Authors copy DLLs into here and
    /// Betta hot-loads them. The MCP server walks the same folder.
    /// </summary>
    public static string PluginFolder
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("BETTA_PLUGIN_FOLDER");
            if (!string.IsNullOrEmpty(env)) return env!;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Grasshopper", "Libraries", "Betta");
        }
    }

    /// <summary>
    /// Rhino 8's <c>System</c> folder (RhinoCommon.dll). <c>BETTA_RHINO_SYSTEM_FOLDER</c> overrides the
    /// default install location.
    /// </summary>
    public static string RhinoSystemFolder
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("BETTA_RHINO_SYSTEM_FOLDER");
            if (!string.IsNullOrEmpty(env)) return env!;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Rhino 8", "System");
        }
    }

    /// <summary>RhinoCommon, Grasshopper and GH_IO in a Rhino install; some may not exist.</summary>
    public static IEnumerable<string> RhinoAssemblies
    {
        get
        {
            var system = RhinoSystemFolder;
            var grasshopper = Path.Combine(Path.GetDirectoryName(system) ?? system, "Plug-ins", "Grasshopper");
            yield return Path.Combine(system, "RhinoCommon.dll");
            yield return Path.Combine(grasshopper, "Grasshopper.dll");
            yield return Path.Combine(grasshopper, "GH_IO.dll");
        }
    }

    /// <summary>
    /// The append-only log Startup writes to via FileLoggerProvider.
    /// </summary>
    public static string LogFile
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("BETTA_LOG_FILE");
            if (!string.IsNullOrEmpty(env)) return env!;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Grasshopper", "Libraries", "Betta.log");
        }
    }
}
