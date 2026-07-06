// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Betta.Mcp.Tools;

/// <summary>
/// betta_validate_signature — heuristic check that a C#-shaped method signature
/// would be acceptable to Betta's ParamVector / OutputPlanner. This is a static
/// string check, not real reflection: it's intended for code-review agents that
/// want fast feedback while drafting a service method, not as a load-time gate.
///
/// Accepted return-type shapes:
/// <list type="bullet">
///   <item>void or one of the supported primitives / Rhino.Geometry types.</item>
///   <item><c>List&lt;T&gt;</c> / <c>IEnumerable&lt;T&gt;</c> with <c>T</c> acceptable.</item>
///   <item>Up to 8-tuples of acceptable types.</item>
///   <item><c>Task</c>, <c>Task&lt;T&gt;</c>, where <c>T</c> is acceptable.</item>
///   <item>Anything in the caller-supplied <c>opaqueTypes</c> list.</item>
/// </list>
/// Parameter type rules mirror return types except <c>Task</c>.
/// </summary>
public sealed class BettaValidateSignatureTool : ITool
{
    public string Name => "betta_validate_signature";
    public string Description =>
        "Heuristically check whether a method signature (e.g. \"double Cube(double x)\") would be accepted by Betta's parameter/output mapper. Returns ok=true/false and a list of problems.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            signature = new
            {
                type = "string",
                description = "A C# method signature: returnType MethodName(paramType paramName, ...). Modifiers like public/static are ignored if present.",
            },
            opaqueTypes = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Optional list of opaque domain types the caller has registered with Betta (Param_BettaGoo<T>). These are treated as always acceptable.",
            },
        },
        required = new[] { "signature" },
        additionalProperties = false,
    };

    // Names accepted by ParamVector.GetGhParamType — primitives, the explicit
    // Rhino.Geometry switch list, and the System.* types it special-cases.
    private static readonly HashSet<string> AcceptedTypeNames = new(StringComparer.Ordinal)
    {
        // primitives by short and long name
        "void", "bool", "Boolean", "System.Boolean",
        "byte", "Byte", "System.Byte",
        "short", "Int16", "System.Int16",
        "int", "Int32", "System.Int32",
        "long", "Int64", "System.Int64",
        "float", "Single", "System.Single",
        "double", "Double", "System.Double",
        "decimal", "Decimal", "System.Decimal",
        "string", "String", "System.String",
        // Rhino.Geometry (anything under the namespace passes — see RhinoGeometryNamespace)
        // System
        "Guid", "System.Guid",
        "Color", "System.Drawing.Color",
        // Task is handled specially in IsAcceptableReturnType
    };

    public Task<object> InvokeAsync(JsonElement? arguments)
    {
        if (!arguments.HasValue
            || arguments.Value.ValueKind != JsonValueKind.Object
            || !arguments.Value.TryGetProperty("signature", out var sigEl)
            || sigEl.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("betta_validate_signature requires { signature: string }.");
        }

        var sig = sigEl.GetString() ?? string.Empty;
        var opaque = new HashSet<string>(StringComparer.Ordinal);
        if (arguments.Value.TryGetProperty("opaqueTypes", out var opaqueEl)
            && opaqueEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in opaqueEl.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) opaque.Add(item.GetString() ?? string.Empty);
        }

        var problems = new List<string>();
        var parsed = TryParse(sig, problems);
        if (parsed is null)
        {
            return Task.FromResult<object>(new { ok = false, problems });
        }

        if (!IsAcceptableReturnType(parsed.ReturnType, opaque, problems))
            problems.Add($"Return type '{parsed.ReturnType}' is not a Betta-known type. Add it to opaqueTypes if your plugin registers it via Param_BettaGoo<T>.");

        foreach (var p in parsed.Parameters)
        {
            if (!IsAcceptableParameterType(p.Type, opaque))
                problems.Add($"Parameter '{p.Name}' has type '{p.Type}' which is not a Betta-known input type.");
        }

        return Task.FromResult<object>(new
        {
            ok = problems.Count == 0,
            parsed = new { returnType = parsed.ReturnType, name = parsed.MethodName, parameters = parsed.Parameters },
            problems,
        });
    }

    // --- parsing ---

    private sealed class ParsedSignature
    {
        public string ReturnType { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
        public List<ParsedParam> Parameters { get; set; } = new();
    }
    private sealed class ParsedParam
    {
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private static ParsedSignature? TryParse(string raw, List<string> problems)
    {
        var s = raw.Trim();
        // Strip common access modifiers and return-keyword adornments so the
        // user can paste `public static double Cube(double x)` and have it
        // parse the same as the bare form.
        foreach (var mod in new[] { "public ", "private ", "internal ", "protected ", "static ", "async ", "virtual ", "override ", "sealed " })
        {
            while (s.StartsWith(mod, StringComparison.Ordinal)) s = s[mod.Length..].TrimStart();
        }

        // Drop optional trailing `;` or `{ ... }` body.
        int braceIdx = s.IndexOf('{');
        if (braceIdx >= 0) s = s[..braceIdx].TrimEnd();
        if (s.EndsWith(";", StringComparison.Ordinal)) s = s[..^1].TrimEnd();

        int parenOpen = s.IndexOf('(');
        int parenClose = s.LastIndexOf(')');
        if (parenOpen < 0 || parenClose < parenOpen)
        {
            problems.Add("Signature must contain `(` and `)`.");
            return null;
        }

        var beforeParen = s[..parenOpen].TrimEnd();
        // Split returnType + methodName on the last whitespace OR the last token boundary
        // that isn't inside angle brackets (so `List<int> Foo` parses correctly).
        var split = SplitOnTopLevelLastSpace(beforeParen);
        if (split is null)
        {
            problems.Add("Could not split return type and method name.");
            return null;
        }
        var (returnType, methodName) = split.Value;

        var paramsRaw = s.Substring(parenOpen + 1, parenClose - parenOpen - 1).Trim();
        var paramList = new List<ParsedParam>();
        if (paramsRaw.Length > 0)
        {
            foreach (var part in SplitOnTopLevelCommas(paramsRaw))
            {
                var partTrim = part.Trim();
                // strip parameter modifiers
                foreach (var mod in new[] { "ref ", "out ", "in ", "params ", "this " })
                {
                    if (partTrim.StartsWith(mod, StringComparison.Ordinal))
                        partTrim = partTrim[mod.Length..].TrimStart();
                }
                // Drop default value: `int x = 0` -> `int x`.
                int eq = partTrim.IndexOf('=');
                if (eq >= 0) partTrim = partTrim[..eq].TrimEnd();

                var p = SplitOnTopLevelLastSpace(partTrim);
                if (p is null)
                {
                    problems.Add($"Could not parse parameter '{partTrim}'.");
                    continue;
                }
                paramList.Add(new ParsedParam { Type = p.Value.left, Name = p.Value.right });
            }
        }

        return new ParsedSignature
        {
            ReturnType = returnType,
            MethodName = methodName,
            Parameters = paramList,
        };
    }

    private static (string left, string right)? SplitOnTopLevelLastSpace(string s)
    {
        int depth = 0, lastSpace = -1;
        for (int i = 0; i < s.Length; i++)
        {
            switch (s[i])
            {
                case '<': depth++; break;
                case '>': depth--; break;
                case ' ' when depth == 0: lastSpace = i; break;
            }
        }
        if (lastSpace < 0) return null;
        return (s[..lastSpace].TrimEnd(), s[(lastSpace + 1)..].TrimStart());
    }

    private static IEnumerable<string> SplitOnTopLevelCommas(string s)
    {
        int depth = 0, start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            switch (s[i])
            {
                case '<': depth++; break;
                case '>': depth--; break;
                case ',' when depth == 0:
                    yield return s.Substring(start, i - start);
                    start = i + 1; break;
            }
        }
        yield return s.Substring(start);
    }

    // --- type acceptance ---

    private static bool IsAcceptableReturnType(string typeText, HashSet<string> opaque, List<string> problems)
    {
        var t = typeText.Trim();
        if (t.Length == 0) { problems.Add("Empty return type."); return false; }

        // Task / Task<T>
        if (t == "Task" || t == "System.Threading.Tasks.Task") return true;
        if (TryUnwrapGeneric(t, "Task", out var inner)
            || TryUnwrapGeneric(t, "System.Threading.Tasks.Task", out inner))
            return IsAcceptableParameterType(inner, opaque);

        return IsAcceptableParameterType(t, opaque);
    }

    private static bool IsAcceptableParameterType(string typeText, HashSet<string> opaque)
    {
        var t = typeText.Trim();
        if (t.Length == 0) return false;
        if (t == "void") return true;

        // Opaque whitelist short-circuits everything.
        if (opaque.Contains(t)) return true;
        var simpleName = StripNamespace(t);
        if (opaque.Contains(simpleName)) return true;

        if (AcceptedTypeNames.Contains(t) || AcceptedTypeNames.Contains(simpleName)) return true;

        // Rhino.Geometry.* — accept the whole namespace. ParamVector knows the
        // most common types explicitly; anything else falls back to Param_GenericObject
        // which Betta still allows.
        if (t.StartsWith("Rhino.Geometry.", StringComparison.Ordinal)) return true;

        // List<T> / IEnumerable<T> / IList<T> / ICollection<T> -> inspect T
        foreach (var wrapper in new[] { "List", "IList", "IEnumerable", "ICollection", "IReadOnlyList", "IReadOnlyCollection" })
        {
            if (TryUnwrapGeneric(t, wrapper, out var inner))
                return IsAcceptableParameterType(inner, opaque);
            if (TryUnwrapGeneric(t, $"System.Collections.Generic.{wrapper}", out inner))
                return IsAcceptableParameterType(inner, opaque);
        }

        // Tuple / ValueTuple of up to 8 args.
        var tupleMatch = Regex.Match(t, @"^(?:System\.)?(?:Value)?Tuple<(.+)>$");
        if (tupleMatch.Success)
        {
            var inners = SplitOnTopLevelCommas(tupleMatch.Groups[1].Value).Select(x => x.Trim()).ToList();
            if (inners.Count > 8) return false;
            return inners.All(x => IsAcceptableParameterType(x, opaque));
        }

        // `(double, double)` shorthand — element types comma-separated inside parens.
        if (t.StartsWith("(", StringComparison.Ordinal) && t.EndsWith(")", StringComparison.Ordinal))
        {
            var inside = t.Substring(1, t.Length - 2);
            var inners = SplitOnTopLevelCommas(inside)
                .Select(x =>
                {
                    var split = SplitOnTopLevelLastSpace(x.Trim());
                    return split.HasValue ? split.Value.left : x.Trim();
                }).ToList();
            if (inners.Count is < 2 or > 8) return false;
            return inners.All(x => IsAcceptableParameterType(x, opaque));
        }

        return false;
    }

    private static bool TryUnwrapGeneric(string text, string outer, out string inner)
    {
        inner = string.Empty;
        var prefix = outer + "<";
        if (!text.StartsWith(prefix, StringComparison.Ordinal) || !text.EndsWith(">", StringComparison.Ordinal))
            return false;
        inner = text.Substring(prefix.Length, text.Length - prefix.Length - 1).Trim();
        return true;
    }

    private static string StripNamespace(string t)
    {
        // Only strip when no generic args present; otherwise namespace lives outside the < >.
        int lt = t.IndexOf('<');
        var bare = lt < 0 ? t : t[..lt];
        int lastDot = bare.LastIndexOf('.');
        return lastDot < 0 ? t : t[(lastDot + 1)..];
    }
}
