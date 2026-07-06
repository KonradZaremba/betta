// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Betta.Services;
using Grasshopper.Kernel;

namespace Betta.Components
{
    /// <summary>
    /// Diagnostic component that lists every descriptor currently registered in
    /// the global <see cref="DescriptorCache"/>. Hand-written (not generated)
    /// because it consumes Betta's own registry rather than a service method
    /// — one of the few cases where the rule against hand-writing GH_Components
    /// makes no sense.
    ///
    /// Output is a single string list: one line per descriptor formatted as
    ///   Category/SubCategory · Name · Method signature · Guid · Source DLL · flags
    /// with columns padded for readability and truncated with … when needed.
    /// </summary>
    public sealed class BettaInspector : GH_Component
    {
        /// <summary>
        /// Well-known stable GUID for the BettaInspector component. Hardcoded
        /// (not derived from a descriptor) because this component is the same
        /// type everywhere — its identity must survive across plugin versions
        /// so saved .gh documents continue to find it.
        /// </summary>
        public static readonly Guid WellKnownGuid =
            new Guid("b117a7e0-1050-4ec7-9e10-bea1ad0d11ee");

        // Column widths chosen so a typical descriptor line stays comfortably
        // under ~140 chars — wide enough to read in Rhino's command pane
        // without forced wrapping at the default font size.
        private const int CatWidth   = 22; // "Betta/Inspector"
        private const int NameWidth  = 20;
        private const int SigWidth   = 40;
        private const int DllWidth   = 24;
        private const string Sep     = " · ";

        public BettaInspector()
            : base("Inspector", "Inspect",
                   "Lists every Betta-registered descriptor (category, name, signature, GUID, source DLL, flags).",
                   "Betta", "Inspector")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // No inputs — output reflects the live registry on every solve.
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter(
                "Descriptors", "D",
                "One line per registered Betta descriptor.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var lines = new List<string>();
            // Sort so the output is stable and easy to scan; ordering by
            // Category then Name mimics the ribbon's own visual grouping.
            var descriptors = DescriptorCache.All
                .OrderBy(d => d.Category ?? string.Empty)
                .ThenBy(d => d.SubCategory ?? string.Empty)
                .ThenBy(d => d.Name ?? string.Empty)
                .ToList();

            foreach (var d in descriptors)
            {
                lines.Add(Format(d));
            }

            if (lines.Count == 0)
            {
                lines.Add("(no Betta descriptors registered)");
            }

            DA.SetDataList(0, lines);
        }

        /// <summary>
        /// Public for the Betta_Status Rhino command, which writes the same
        /// per-descriptor line to RhinoApp.WriteLine.
        /// </summary>
        public static string Format(ComponentDescriptor d)
        {
            var cat = Pad($"{d.Category ?? "?"}/{d.SubCategory ?? "?"}", CatWidth);
            var name = Pad(d.Name ?? "(unnamed)", NameWidth);
            var sig = Pad(MethodSignature(d.Method), SigWidth);
            var guid = d.Guid.ToString("D");
            var dll = Pad(SourceDllName(d.ServiceType), DllWidth);
            var flags = FlagsText(d);

            var sb = new StringBuilder();
            sb.Append(cat).Append(Sep)
              .Append(name).Append(Sep)
              .Append(sig).Append(Sep)
              .Append(guid).Append(Sep)
              .Append(dll).Append(Sep)
              .Append(flags);
            return sb.ToString();
        }

        private static string FlagsText(ComponentDescriptor d)
        {
            var parts = new List<string>(4);
            if (!d.Enabled)      parts.Add("[disabled]");
            if (d.HasPreview)    parts.Add("[preview]");
            if (d.HasBakeable)   parts.Add("[bake]");
            return parts.Count == 0 ? string.Empty : string.Join("", parts);
        }

        private static string MethodSignature(MethodInfo m)
        {
            if (m == null) return "(no method)";
            var args = string.Join(", ",
                m.GetParameters().Select(p => $"{ShortTypeName(p.ParameterType)} {p.Name}"));
            return $"{ShortTypeName(m.ReturnType)} {m.Name}({args})";
        }

        private static string ShortTypeName(Type t)
        {
            if (t == null) return "?";
            if (t == typeof(void)) return "void";
            // Strip namespace; keep generic arity intact (e.g. List<Double>).
            if (t.IsGenericType)
            {
                var raw = t.Name;
                var tick = raw.IndexOf('`');
                var bare = tick > 0 ? raw.Substring(0, tick) : raw;
                var inner = string.Join(", ", t.GetGenericArguments().Select(ShortTypeName));
                return $"{bare}<{inner}>";
            }
            return t.Name;
        }

        private static string SourceDllName(Type serviceType)
        {
            if (serviceType == null) return "?";
            try
            {
                var loc = serviceType.Assembly.Location;
                return string.IsNullOrEmpty(loc)
                    ? serviceType.Assembly.GetName().Name + ".dll"
                    : Path.GetFileName(loc);
            }
            catch
            {
                return serviceType.Assembly.GetName().Name + ".dll";
            }
        }

        /// <summary>
        /// Right-pad to <paramref name="width"/>; if the input overflows,
        /// truncate and append "…" so the total visual length is still
        /// <paramref name="width"/>.
        /// </summary>
        private static string Pad(string s, int width)
        {
            s ??= string.Empty;
            if (s.Length == width) return s;
            if (s.Length > width)
            {
                if (width <= 1) return "…";
                return s.Substring(0, width - 1) + "…";
            }
            return s.PadRight(width);
        }

        /// <summary>
        /// No fish icon — BettaInspector is a hand-written component with no
        /// descriptor of its own, so the fish picker (which keys off a
        /// descriptor GUID) has nothing to pick from. Returning null lets
        /// Grasshopper render its default placeholder. If we later want a
        /// dedicated PNG, drop it next to the Mini bettas and embed it.
        /// </summary>
        protected override Bitmap Icon => null;

        public override Guid ComponentGuid => WellKnownGuid;

        // Show up in the toolbar. BettaComponent forces 'hidden' to suppress
        // GH's auto-discovery of the generic descriptor-backed type — that
        // suppression doesn't apply here, so we restate 'primary' explicitly.
        public override GH_Exposure Exposure => GH_Exposure.primary;
    }
}
