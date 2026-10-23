// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Compiled into Betta.Generators and, as a linked file, into Betta.Mcp, so the build and the
// MCP server judge a lock the same way. No Roslyn types here.
#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Betta.Lock
{
    /// <summary>
    /// One line of betta.lock: <c>guid Type.Method</c>, or <c>guid Type.Method(ParamType, ...)</c>
    /// for an overload. <see cref="Line"/> is 0-based.
    /// </summary>
    internal sealed class LockEntry
    {
        public Guid Guid { get; }
        public string Label { get; }
        public string TypeName { get; }
        public string MethodName { get; }
        public bool IsOverload { get; }
        public int Line { get; }

        public LockEntry(Guid guid, string label, int line)
        {
            Guid = guid;
            Label = label;
            Line = line;
            var paren = label.IndexOf('(');
            IsOverload = paren >= 0;
            var name = IsOverload ? label.Substring(0, paren) : label;
            var dot = name.LastIndexOf('.');
            TypeName = dot > 0 ? name.Substring(0, dot) : "";
            MethodName = dot > 0 ? name.Substring(dot + 1) : name;
        }
    }

    /// <summary>
    /// A <c>[GrasshopperMethod]</c> as the lock sees it. <see cref="Source"/> is the caller's own
    /// handle on it (the generator's method model, for its location).
    /// </summary>
    internal sealed class LockComponent
    {
        public string TypeName { get; }
        public string MethodName { get; }
        public bool Overloaded { get; }
        public IReadOnlyList<string> ParameterTypes { get; }
        public Guid Guid { get; }
        public IReadOnlyCollection<Guid> FormerGuids { get; }
        public bool Enabled { get; }
        public object Source { get; }
        public string Label => GuidLockCore.Label(TypeName, MethodName, Overloaded, ParameterTypes);

        public LockComponent(string typeName, string methodName, bool overloaded, IReadOnlyList<string> parameterTypes,
            Guid guid, IReadOnlyCollection<Guid> formerGuids, bool enabled, object source = null)
        {
            TypeName = typeName;
            MethodName = methodName;
            Overloaded = overloaded;
            ParameterTypes = parameterTypes;
            Guid = guid;
            FormerGuids = formerGuids;
            Enabled = enabled;
            Source = source;
        }
    }

    /// <summary>The edit that makes a gone component answer to its locked GUID again.</summary>
    internal enum LockFix
    {
        None,
        /// <summary>Add <see cref="LockFinding.FixValue"/> to FormerNames on the target's [GrasshopperMethod].</summary>
        MethodFormerName,
        /// <summary>Add <see cref="LockFinding.FixValue"/> to FormerNames on the target's [GrasshopperCollection].</summary>
        CollectionFormerName,
        /// <summary>Set Guid = <see cref="LockFinding.FixValue"/> on the target's [GrasshopperMethod].</summary>
        PinGuid,
    }

    /// <summary>
    /// A locked component nothing answers to. <see cref="Target"/> is the component the fix goes on,
    /// or null when no single one is likely.
    /// </summary>
    internal sealed class LockFinding
    {
        public LockEntry Entry { get; }
        public string Hint { get; }
        public LockComponent Target { get; }
        public LockFix Fix { get; }
        public string FixValue { get; }

        public LockFinding(LockEntry entry, string hint, LockComponent target = null, LockFix fix = LockFix.None, string fixValue = null)
        {
            Entry = entry;
            Hint = hint;
            Target = target;
            Fix = target == null ? LockFix.None : fix;
            FixValue = fixValue;
        }

        public string Message => string.Format(GuidLockCore.MessageFormat, Entry.Label, Entry.Guid.ToString("D"), Hint);
    }

    internal sealed class LockResult
    {
        /// <summary>Locked components nothing answers to: renames that break saved files.</summary>
        public List<LockFinding> Gone { get; }

        /// <summary>Published components not in the lock yet, by label.</summary>
        public List<LockComponent> Unlocked { get; }

        /// <summary>What the build adds: nothing while <see cref="Gone"/> has findings, so a rename's new GUID waits until it's resolved.</summary>
        public List<LockComponent> ToAdd => Gone.Count == 0 ? Unlocked : new List<LockComponent>();

        public LockResult(List<LockFinding> gone, List<LockComponent> unlocked)
        {
            Gone = gone;
            Unlocked = unlocked;
        }
    }

    internal static class GuidLockCore
    {
        public const string FileName = "betta.lock";

        public const string MessageFormat =
            "'{0}' is in betta.lock, but no [GrasshopperMethod] answers to its GUID {1} any more, so saved .gh files that use it will show it as missing. {2} If it was removed on purpose, or never released, delete its line from betta.lock.";

        /// <summary>The label a component gets in betta.lock.</summary>
        public static string Label(string typeName, string methodName, bool overloaded, IReadOnlyList<string> parameterTypes) =>
            typeName + "." + methodName + (overloaded ? "(" + string.Join(", ", parameterTypes) + ")" : "");

        /// <summary>The line the build appends for a component.</summary>
        public static string Line(LockComponent component) => component.Guid.ToString("D") + " " + component.Label;

        /// <summary>The lock's entries; blank lines, <c>#</c> comments and lines without a GUID are skipped.</summary>
        public static List<LockEntry> Parse(IEnumerable<string> lines)
        {
            var entries = new List<LockEntry>();
            var index = -1;
            foreach (var raw in lines)
            {
                index++;
                var s = raw.Trim();
                if (s.Length == 0 || s[0] == '#') continue;
                var space = s.IndexOfAny(new[] { ' ', '\t' });
                if (!Guid.TryParse(space < 0 ? s : s.Substring(0, space), out var guid)) continue;
                entries.Add(new LockEntry(guid, space < 0 ? "" : s.Substring(space + 1).Trim(), index));
            }
            return entries;
        }

        /// <summary>
        /// Which locked GUIDs nothing answers to (as a component's GUID, pinned or derived, or
        /// through FormerNames), and which components aren't locked yet. Disabled components
        /// count as answering, since disabling one is deliberate, but are never added: Betta
        /// doesn't publish them, so no saved file holds them.
        /// </summary>
        public static LockResult Check(IReadOnlyList<LockEntry> entries, IReadOnlyCollection<LockComponent> components)
        {
            var locked = new HashSet<Guid>(entries.Select(e => e.Guid));
            var answered = new HashSet<Guid>(components.SelectMany(c => c.FormerGuids.Concat(new[] { c.Guid })));
            var unlocked = components
                .Where(c => c.Enabled && !locked.Contains(c.Guid))
                .GroupBy(c => c.Guid).Select(g => g.First())
                .OrderBy(c => c.Label, StringComparer.Ordinal)
                .ToList();
            var gone = entries
                .Where(e => !answered.Contains(e.Guid))
                .GroupBy(e => e.Guid).Select(g => g.First())
                .Select(e => Explain(e, components, unlocked))
                .ToList();
            return new LockResult(gone, unlocked);
        }

        /// <summary>What most likely happened to a locked component, judged from the components not locked yet.</summary>
        private static LockFinding Explain(LockEntry e, IReadOnlyCollection<LockComponent> components, List<LockComponent> unlocked)
        {
            var sameType = components.Where(c => c.TypeName == e.TypeName).ToList();
            var sameName = sameType.Where(c => c.MethodName == e.MethodName).ToList();
            var pin = e.Guid.ToString("D");

            if (e.IsOverload && sameName.Count > 0)
                return new LockFinding(e,
                    $"An overload's GUID includes its parameters and return type, so changing them, or removing the other overloads, changed it. Keep it by adding Guid = \"{pin}\" to the method that should answer to it.",
                    sameName.Count == 1 ? sameName[0] : null, LockFix.PinGuid, pin);

            if (!e.IsOverload && sameName.Any(c => c.Overloaded))
                return new LockFinding(e,
                    $"Adding an overload of '{e.MethodName}' changed its GUID: add FormerNames = new[] {{ \"{e.MethodName}\" }} to the overload that had the name first.");

            // Same type, same name, no overload: only a pinned Guid changes that.
            if (!e.IsOverload && sameName.Count > 0)
                return new LockFinding(e,
                    $"'{e.MethodName}' has a pinned Guid now, which replaces the one saved files use: pin Guid = \"{pin}\" instead, or remove the pin.",
                    sameName[0], LockFix.PinGuid, pin);

            if (sameType.Count > 0)
            {
                var renamed = unlocked.Where(c => c.TypeName == e.TypeName).ToList();
                if (renamed.Count == 1)
                    return new LockFinding(e,
                        $"If it was renamed to '{renamed[0].MethodName}', add FormerNames = new[] {{ \"{e.MethodName}\" }} to that method.",
                        renamed[0], LockFix.MethodFormerName, e.MethodName);
                var moved = unlocked.Where(c => c.MethodName == e.MethodName).ToList();
                if (moved.Count == 1)
                    return new LockFinding(e,
                        $"If it moved to '{moved[0].TypeName}', add FormerNames = new[] {{ \"{e.TypeName}.{e.MethodName}\" }} to that method.",
                        moved[0], LockFix.MethodFormerName, e.TypeName + "." + e.MethodName);
            }
            else
            {
                // The whole type is gone: renamed, or moved to another namespace.
                var target = unlocked.Where(c => c.MethodName == e.MethodName).ToList();
                if (target.Select(c => c.TypeName).Distinct().Count() == 1)
                    return new LockFinding(e,
                        $"If '{e.TypeName}' was renamed or moved to '{target[0].TypeName}', add FormerNames = new[] {{ \"{e.TypeName}\" }} to the [GrasshopperCollection] on '{target[0].TypeName}'.",
                        target[0], LockFix.CollectionFormerName, e.TypeName);
            }

            return new LockFinding(e,
                "If it was renamed or moved, list its old name in FormerNames: the method name, or \"Type.Method\" if it moved type, on [GrasshopperMethod]; the old type name on [GrasshopperCollection].");
        }
    }
}
