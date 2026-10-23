// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using static Betta.Generators.BettaComponentGenerator;

namespace Betta.Generators
{
    /// <summary>
    /// <c>betta.lock</c>: the GUID of every component the plugin has published, one
    /// <c>guid Type.Method</c> line each, checked in next to the project. A locked GUID that no
    /// <c>[GrasshopperMethod]</c> answers to any more (as its GUID, pinned or derived, or through
    /// <c>FormerNames</c>) is a rename that breaks saved .gh files, reported as BETTA012.
    /// The generator only reads the file: it lists components not yet locked in
    /// <see cref="PendingHintName"/>, and the Betta.Generators build targets append them.
    /// Nothing is added while BETTA012 is reported, so a rename's new GUID never takes the
    /// old one's place in the lock before the rename is resolved.
    /// </summary>
    internal static class GuidLock
    {
        internal const string FileName = "betta.lock";
        internal const string PendingHintName = "BettaLockPending.g.cs";
        // Betta.Generators.targets matches this prefix.
        internal const string PendingPrefix = "// add: ";

        internal static readonly DiagnosticDescriptor BETTA012_LockedComponentGone = new(
            id: "BETTA012",
            title: "A component in betta.lock no longer exists",
            messageFormat: "'{0}' is in betta.lock, but no [GrasshopperMethod] answers to its GUID {1} any more, so saved .gh files that use it will show it as missing. {2} If it was removed on purpose, or never released, delete its line from betta.lock.",
            category: "Betta",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        internal sealed class Entry
        {
            public Guid Guid { get; }
            public string Label { get; }
            public string TypeName { get; }
            public string MethodName { get; }
            public bool IsOverload { get; }
            public Location Location { get; }

            public Entry(Guid guid, string label, Location location)
            {
                Guid = guid;
                Label = label;
                Location = location;
                // Type.Method, or Type.Method(ParamType, ...) for an overload.
                var paren = label.IndexOf('(');
                IsOverload = paren >= 0;
                var name = IsOverload ? label.Substring(0, paren) : label;
                var dot = name.LastIndexOf('.');
                TypeName = dot > 0 ? name.Substring(0, dot) : "";
                MethodName = dot > 0 ? name.Substring(dot + 1) : name;
            }
        }

        /// <summary>The label a component gets in betta.lock.</summary>
        internal static string Label(ServiceTypeModel type, MethodModel method) =>
            type.FullName + "." + method.MethodName +
            (method.Overloaded ? "(" + string.Join(", ", method.Parameters.Select(p => p.TypeFullName)) + ")" : "");

        /// <summary>The lock's entries; blank lines, <c>#</c> comments and lines without a GUID are skipped.</summary>
        internal static List<Entry> Parse(AdditionalText file, CancellationToken ct)
        {
            var entries = new List<Entry>();
            var text = file.GetText(ct);
            if (text == null) return entries;
            foreach (var line in text.Lines)
            {
                var s = line.ToString().Trim();
                if (s.Length == 0 || s[0] == '#') continue;
                var space = s.IndexOfAny(new[] { ' ', '\t' });
                if (!Guid.TryParse(space < 0 ? s : s.Substring(0, space), out var guid)) continue;
                var label = space < 0 ? "" : s.Substring(space + 1).Trim();
                entries.Add(new Entry(guid, label, Location.Create(file.Path, line.Span, text.Lines.GetLinePositionSpan(line.Span))));
            }
            return entries;
        }

        /// <summary>
        /// Reports BETTA012 for each locked GUID nothing answers to, and emits the components
        /// to add to the lock. <paramref name="lockFile"/> is null before the first build writes it.
        /// </summary>
        internal static void Run(SourceProductionContext spc, IReadOnlyCollection<ServiceTypeModel> types, AdditionalText lockFile)
        {
            var entries = lockFile == null ? new List<Entry>() : Parse(lockFile, spc.CancellationToken);
            var locked = new HashSet<Guid>(entries.Select(e => e.Guid));
            var answered = new HashSet<Guid>(types.SelectMany(t => t.Methods).SelectMany(m => m.FormerGuids.Concat(new[] { m.Guid })));
            // Disabled methods are never published, so no saved file holds them.
            var unlocked = types
                .SelectMany(t => t.Methods.Where(m => m.Enabled && !locked.Contains(m.Guid)).Select(m => (Type: t, Method: m)))
                .GroupBy(u => u.Method.Guid).Select(g => g.First())
                .ToList();

            var gone = entries.Where(e => !answered.Contains(e.Guid)).GroupBy(e => e.Guid).Select(g => g.First()).ToList();
            foreach (var e in gone)
            {
                var (hint, at) = Hint(e, types, unlocked);
                spc.ReportDiagnostic(Diagnostic.Create(BETTA012_LockedComponentGone,
                    at ?? e.Location, at != null ? new[] { e.Location } : null,
                    e.Label, e.Guid.ToString("D"), hint));
            }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/> Betta source generator.");
            sb.AppendLine("// The components this build adds to betta.lock, read by the Betta.Generators");
            sb.AppendLine("// build targets. Nothing in this file is compiled.");
            if (gone.Count > 0)
                sb.AppendLine("// Nothing is added while betta.lock has components nothing answers to (BETTA012).");
            else
                foreach (var (guid, label) in unlocked.Select(u => (u.Method.Guid, Label(u.Type, u.Method))).OrderBy(x => x.Item2, StringComparer.Ordinal))
                    sb.AppendLine(PendingPrefix + guid.ToString("D") + " " + label);
            spc.AddSource(PendingHintName, sb.ToString());
        }

        /// <summary>
        /// What most likely happened to a locked component, judged from the components not
        /// locked yet, and where to fix it: the method to add FormerNames to, or null for the lock line.
        /// </summary>
        private static (string Hint, Location At) Hint(Entry e, IReadOnlyCollection<ServiceTypeModel> types,
            List<(ServiceTypeModel Type, MethodModel Method)> unlocked)
        {
            var sameType = types.Where(t => t.FullName == e.TypeName).SelectMany(t => t.Methods).ToList();
            var sameName = sameType.Where(m => m.MethodName == e.MethodName).ToList();

            if (e.IsOverload && sameName.Count > 0)
                return ($"An overload's GUID includes its parameters and return type, so changing them, or removing the other overloads, changed it. Keep it by adding Guid = \"{e.Guid:D}\" to the method that should answer to it.",
                    sameName.Count == 1 ? sameName[0].Location : null);

            if (!e.IsOverload && sameName.Any(m => m.Overloaded))
                return ($"Adding an overload of '{e.MethodName}' changed its GUID: add FormerNames = new[] {{ \"{e.MethodName}\" }} to the overload that had the name first.", null);

            // Same type, same name, no overload: only a pinned Guid changes that.
            if (!e.IsOverload && sameName.Count > 0)
                return ($"'{e.MethodName}' has a pinned Guid now, which replaces the one saved files use: pin Guid = \"{e.Guid:D}\" instead, or remove the pin.", sameName[0].Location);

            if (sameType.Count > 0)
            {
                var renamed = unlocked.Where(u => u.Type.FullName == e.TypeName).ToList();
                if (renamed.Count == 1)
                    return ($"If it was renamed to '{renamed[0].Method.MethodName}', add FormerNames = new[] {{ \"{e.MethodName}\" }} to that method.", renamed[0].Method.Location);
                var moved = unlocked.Where(u => u.Method.MethodName == e.MethodName).ToList();
                if (moved.Count == 1)
                    return ($"If it moved to '{moved[0].Type.FullName}', add FormerNames = new[] {{ \"{e.TypeName}.{e.MethodName}\" }} to that method.", moved[0].Method.Location);
            }
            else
            {
                // The whole type is gone: renamed, or moved to another namespace.
                var target = unlocked.Where(u => u.Method.MethodName == e.MethodName).ToList();
                if (target.Select(u => u.Type.FullName).Distinct().Count() == 1)
                    return ($"If '{e.TypeName}' was renamed or moved to '{target[0].Type.FullName}', add FormerNames = new[] {{ \"{e.TypeName}\" }} to the [GrasshopperCollection] on '{target[0].Type.FullName}'.", target[0].Method.Location);
            }

            return ("If it was renamed or moved, list its old name in FormerNames: the method name, or \"Type.Method\" if it moved type, on [GrasshopperMethod]; the old type name on [GrasshopperCollection].", null);
        }
    }
}
