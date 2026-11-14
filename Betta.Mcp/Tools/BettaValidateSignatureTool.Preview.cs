// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.RegularExpressions;

namespace Betta.Mcp.Tools;

/// <summary>
/// The component preview half of betta_validate_signature: the inputs and outputs Betta will create from a signature,
/// with their names, and hints that give the exact attribute to write when a name is only a default.
///
/// It follows the runtime's naming rules:
/// <list type="bullet">
///   <item>Inputs (ParamVector): <c>[GrasshopperParameter("…")]</c>, else the C# parameter name as written.</item>
///   <item>Outputs (OutputPlanner): <c>[GrasshopperOutput]</c> (by <c>Index</c> for tuples), else tuple element names
///     split into words, else "Output" / "Output1".."OutputN"; an unknown class explodes into property outputs.</item>
/// </list>
/// Agent traces showed why this matters: asked for an output called "Result", agents spent 2–4 turns searching the
/// package docs for how to name a single output, and an ok=true answer didn't settle it.
/// </summary>
public sealed partial class BettaValidateSignatureTool
{
    private sealed record ParsedAttribute(string Target, string Name, string Args);

    private sealed record OutputAttribute(string? Name, int? Index);

    private sealed record Pin(string Name, string Access, string NameFrom);

    private static object BuildComponent(ParsedSignature sig, HashSet<string> opaque, out List<string> hints)
    {
        hints = new List<string>();

        var inputs = new List<Pin>();
        foreach (var p in sig.Parameters.Where(p => p.Wired))
            inputs.Add(new Pin(p.DisplayName ?? p.Name, Access(p.Type, Position.Parameter), p.DisplayName != null ? "attribute" : "parameter name"));

        // The example in the hint: an unnamed input whose words split ("sourceMin"), else any that reads differently.
        var candidates = sig.Parameters.Where(p => p.Wired && p.DisplayName == null && SplitWords(p.Name) != p.Name).ToList();
        var unnamed = candidates.FirstOrDefault(p => SplitWords(p.Name).Contains(' ')) ?? candidates.FirstOrDefault();
        if (unnamed != null)
        {
            string shown = SplitWords(unnamed.Name);
            hints.Add($"Inputs are named after the C# parameters, so '{unnamed.Name}' shows as \"{unnamed.Name}\". " +
                      $"For \"{shown}\", write [GrasshopperParameter(\"{shown}\")] {unnamed.Type} {unnamed.Name}.");
        }

        var outputs = PlanOutputs(sig.ReturnType, sig.OutputAttributes, opaque);
        if (outputs.Count == 1 && outputs[0].NameFrom == "default")
            hints.Add("The output is named \"Output\". To name it, put [return: GrasshopperOutput(\"Result\")] on the method " +
                      "(with your name instead of Result).");
        else if (outputs.Count > 1 && outputs.Any(o => o.NameFrom == "default"))
            hints.Add("Unnamed tuple elements become Output1..OutputN. Name them in the return type, e.g. (double Sum, double Average), " +
                      "or with [return: GrasshopperOutput(\"Sum\", Index = 0)].");

        return new { name = sig.MethodName, inputs, outputs };
    }

    private static List<Pin> PlanOutputs(string returnType, List<OutputAttribute> attributes, HashSet<string> opaque)
    {
        var t = returnType.Trim();
        // Async / streaming wrappers, as CheckReturnType unwraps them.
        for (int guard = 0; guard < 4; guard++)
        {
            if (StripNamespace(t) is "Task" or "ValueTask") return new List<Pin>();
            if (TryGetGeneric(t, out var wrapper, out var inner) && inner.Count == 1 && wrapper is "Task" or "ValueTask" or "IObservable")
            {
                t = inner[0].Trim();
                continue;
            }
            break;
        }
        if (t is "void" or "") return new List<Pin>();
        if (t.EndsWith("?", StringComparison.Ordinal)) t = t[..^1];

        // Tuples: one output per element.
        List<(string Type, string? Name)>? elements = null;
        if (t.StartsWith("(", StringComparison.Ordinal) && t.EndsWith(")", StringComparison.Ordinal))
        {
            elements = SplitOnTopLevelCommas(t[1..^1])
                .Select(e => e.Trim())
                .Select(e => SplitOnTopLevelLastSpace(e) is { } sp ? (sp.left, (string?)sp.right) : (e, null))
                .ToList();
        }
        else if (TryGetGeneric(t, out var tupleName, out var tupleArgs) && tupleName is "ValueTuple" or "Tuple")
        {
            elements = tupleArgs.Select(a => (a, (string?)null)).ToList();
        }
        if (elements != null)
        {
            var pins = new List<Pin>();
            for (int i = 0; i < elements.Count; i++)
            {
                var byIndex = attributes.FirstOrDefault(a => (a.Index ?? 0) == i && a.Name != null);
                var (type, name) = elements[i];
                if (byIndex != null) pins.Add(new Pin(byIndex.Name!, Access(type, Position.Element), "attribute"));
                else if (name != null) pins.Add(new Pin(SplitWords(name), Access(type, Position.Element), "tuple element"));
                else pins.Add(new Pin($"Output{i + 1}", Access(type, Position.Element), "default"));
            }
            return pins;
        }

        // One output. An unknown class at the top explodes into its properties instead.
        var single = attributes.FirstOrDefault(a => a.Name != null);
        if (IsExplodedClass(t, opaque))
            return new List<Pin> { new($"(one per public property of {t})", "item", "properties") };
        return new List<Pin> { new(single?.Name ?? "Output", Access(t, Position.ReturnTop), single?.Name != null ? "attribute" : "default") };
    }

    private static string Access(string typeText, Position pos)
    {
        var t = typeText.Trim();
        if (t.EndsWith("?", StringComparison.Ordinal)) t = t[..^1];
        if (TryGetGeneric(t, out var name, out _))
        {
            if (name == "List") return "list";
            if (name == "GH_Structure") return "tree";
        }
        // Arrays are list inputs; as a return value Classify notes they are not list outputs.
        if (t.EndsWith("]", StringComparison.Ordinal) && pos != Position.ReturnTop) return "list";
        return "item";
    }

    private static bool IsExplodedClass(string t, HashSet<string> opaque)
    {
        if (opaque.Contains(t) || opaque.Contains(StripNamespace(t))) return false;
        if (KnownTypeNames.Contains(t) || KnownTypeNames.Contains(StripNamespace(t))) return false;
        if (t.StartsWith("Rhino.Geometry.", StringComparison.Ordinal) || ImageTypeNames.Contains(t)) return false;
        if (TryGetGeneric(t, out _, out _)) return false;   // List, GH_Structure, degraded collections: one output
        // An array return is treated as a class (Classify notes it); an interface ships as one generic output.
        if (t.EndsWith("]", StringComparison.Ordinal)) return true;
        var bare = StripNamespace(t);
        return !(bare.Length > 1 && bare[0] == 'I' && char.IsUpper(bare[1]));
    }

    /// <summary>The name in [Attr("Name", …)] or [Attr(Name = "Name")]; null when there is none.</summary>
    private static string? AttributeName(string? args)
    {
        if (string.IsNullOrWhiteSpace(args)) return null;
        var parts = SplitOnTopLevelCommas(args).Select(p => p.Trim()).ToList();
        if (parts.Count > 0 && TryUnquote(parts[0], out var positional)) return positional;
        foreach (var part in parts)
        {
            var m = Regex.Match(part, @"^Name\s*=\s*(.+)$");
            if (m.Success && TryUnquote(m.Groups[1].Value.Trim(), out var named)) return named;
        }
        return null;
    }

    /// <summary>[GrasshopperOutput(…, Index = n)] or the fourth positional argument (name, nickName, description, index).</summary>
    private static int? AttributeIndex(string? args)
    {
        if (string.IsNullOrWhiteSpace(args)) return null;
        var parts = SplitOnTopLevelCommas(args).Select(p => p.Trim()).ToList();
        foreach (var part in parts)
        {
            var m = Regex.Match(part, @"^Index\s*=\s*(\d+)$");
            if (m.Success) return int.Parse(m.Groups[1].Value);
        }
        return parts.Count >= 4 && int.TryParse(parts[3], out var i) ? i : null;
    }

    private static bool TryUnquote(string literal, out string value)
    {
        value = string.Empty;
        if (literal.StartsWith("@\"", StringComparison.Ordinal) && literal.EndsWith("\"", StringComparison.Ordinal) && literal.Length >= 3)
        {
            value = literal[2..^1].Replace("\"\"", "\"");
            return true;
        }
        if (!literal.StartsWith("\"", StringComparison.Ordinal) || !literal.EndsWith("\"", StringComparison.Ordinal) || literal.Length < 2)
            return false;
        var sb = new StringBuilder();
        var body = literal[1..^1];
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] == '\\' && i + 1 < body.Length)
            {
                char n = body[++i];
                sb.Append(n switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => n });
            }
            else sb.Append(body[i]);
        }
        value = sb.ToString();
        return true;
    }

    /// <summary>
    /// Copy of the runtime's OutputPlanner.SplitWords (Betta.Docs keeps another): "FlatCount" gives "Flat Count",
    /// "CSVPath" "CSV Path", "UCount" "U Count"; "IDs" stays whole. Change them together.
    /// </summary>
    internal static string SplitWords(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return identifier;
        var sb = new StringBuilder(identifier.Length + 4);
        for (int i = 0; i < identifier.Length; i++)
        {
            char c = identifier[i];
            if (c == '_')
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                continue;
            }
            if (i > 0 && char.IsUpper(c) && sb.Length > 0 && sb[sb.Length - 1] != ' ')
            {
                char prev = identifier[i - 1];
                bool next = i + 1 < identifier.Length && char.IsLower(identifier[i + 1]);
                bool plural = next && identifier[i + 1] == 's' &&
                              (i + 2 == identifier.Length || !char.IsLower(identifier[i + 2]));
                if (char.IsLower(prev) || (char.IsUpper(prev) && next && !plural)) sb.Append(' ');
            }
            sb.Append(c);
        }
        var words = sb.ToString().Trim();
        return words.Length == 0 ? identifier : char.ToUpperInvariant(words[0]) + words.Substring(1);
    }
}
