// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Betta.Mcp.Tools;

/// <summary>
/// betta_validate_signature — heuristic check that a C#-shaped method signature
/// would be accepted by Betta's runtime (ParamInjector / ParamVector / OutputPlanner /
/// BettaComponent). This is a static string check, not real reflection: it's
/// intended for code-review agents that want fast feedback while drafting a
/// service method, not as a load-time gate.
///
/// The runtime is permissive: it rejects almost nothing and instead degrades
/// types it does not know. So <c>problems</c> is reserved for signatures that
/// cannot work (unparseable, empty/void parameter types, ill-formed tuples) and
/// <c>notes</c> (additive) reports every degradation while <c>ok</c> stays true.
///
/// What the validator models:
/// <list type="bullet">
///   <item>Primitives, string, decimal, DateTime, Guid, Color, <c>Rhino.Geometry.*</c>.</item>
///   <item>Lists: <c>List&lt;T&gt;</c> and arrays (<c>T[]</c>, <c>params T[]</c>) are list inputs;
///     other collection interfaces degrade to a generic-object input / single output (noted).</item>
///   <item>Tuples of any arity (<c>(a, b)</c>, <c>ValueTuple&lt;..&gt;</c>, <c>Tuple&lt;..&gt;</c>;
///     8+ element ValueTuples flatten via TRest) — one output per element.</item>
///   <item><c>Task</c>, <c>Task&lt;T&gt;</c>, <c>ValueTask</c>, <c>ValueTask&lt;T&gt;</c> (async) and
///     <c>IObservable&lt;T&gt;</c> (streaming) returns are unwrapped.</item>
///   <item>Synthetic parameters: <c>CancellationToken</c>, <c>IProgress&lt;T&gt;</c> and <c>IBettaReport</c> get no input pin;
///     parameters marked <c>[GrasshopperSecret]</c>, <c>[GrasshopperMenuState]</c> or
///     <c>[GrasshopperTrigger]</c> are menu/credential driven and get none either.
///     C# attributes in the signature text are tolerated and stripped.</item>
///   <item><c>GH_Structure&lt;T&gt;</c> — tree input / output.</item>
///   <item><c>Bitmap</c> / <c>Image</c> — one generic-object wire (noted).</item>
///   <item>Caller-listed <c>opaqueTypes</c> — always a single typed wire.</item>
///   <item>Anything else — accepted, with a note: unknown parameter types (including enums,
///     which a string check cannot tell from classes) become a generic-object input; unknown
///     class returns explode into property outputs (or a single generic output).</item>
/// </list>
/// </summary>
public sealed class BettaValidateSignatureTool : ITool
{
    public string Name => "betta_validate_signature";
    public string Title => "Validate Betta method signature";
    public string Description =>
        "Heuristically check whether a method signature (e.g. \"double Cube(double x)\") would be accepted by Betta's runtime, including async/streaming returns, tuples of any arity, arrays/params, CancellationToken/IProgress<T>/IBettaReport and [GrasshopperSecret/Trigger/MenuState] parameters. Returns ok=true/false, a list of problems, and notes where the runtime accepts but degrades a type (unknown class -> property outputs, unknown parameter -> generic object).";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            signature = new
            {
                type = "string",
                description = "A C# method signature: returnType MethodName(paramType paramName, ...). Modifiers like public/static and parameter attributes like [GrasshopperSecret(\"svc.key\")] are tolerated.",
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

    // Types the runtime maps explicitly (ParamVector.GetGhParamType) or via
    // TypeCode (GetGhParamGenericType), plus the OutputPlanner "simple" set.
    private static readonly HashSet<string> KnownTypeNames = new(StringComparer.Ordinal)
    {
        "bool", "Boolean", "System.Boolean",
        "byte", "Byte", "System.Byte",
        "short", "Int16", "System.Int16",
        "int", "Int32", "System.Int32",
        "long", "Int64", "System.Int64",
        "float", "Single", "System.Single",
        "double", "Double", "System.Double",
        "decimal", "Decimal", "System.Decimal",
        "string", "String", "System.String",
        "DateTime", "System.DateTime",
        "Guid", "System.Guid",
        "Color", "System.Drawing.Color",
        // Rhino.Geometry types ParamVector maps to a typed param, as written after `using Rhino.Geometry;`.
        // Fully qualified Rhino.Geometry.* names are accepted separately in Classify.
        "Arc", "BoundingBox", "Box", "Brep", "Circle", "Curve", "Interval", "Line", "Mesh",
        "Plane", "Point3d", "Polyline", "Surface", "Transform", "Vector3d",
    };

    private static readonly HashSet<string> ImageTypeNames = new(StringComparer.Ordinal)
    {
        "Bitmap", "System.Drawing.Bitmap", "Image", "System.Drawing.Image",
    };

    // Collection shapes that are NOT List<T>: ParamVector only treats non-generic
    // IList implementers (List<T>, arrays) as list access.
    private static readonly HashSet<string> DegradedCollectionNames = new(StringComparer.Ordinal)
    {
        "IEnumerable", "IList", "ICollection", "IReadOnlyList", "IReadOnlyCollection",
    };

    private static readonly string[] SignatureModifiers =
        { "public ", "private ", "internal ", "protected ", "static ", "async ", "virtual ", "override ", "sealed ", "extern ", "unsafe ", "new " };

    private static readonly string[] ParameterModifiers = { "ref ", "out ", "in ", "params ", "this ", "scoped " };

    // Parameter attributes that remove the input pin (menu / credential / trigger driven).
    private static readonly HashSet<string> PinlessAttributes = new(StringComparer.Ordinal)
    {
        "GrasshopperSecret", "GrasshopperMenuState", "GrasshopperTrigger",
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
        var notes = new List<string>();
        var parsed = TryParse(sig, problems);
        if (parsed is null)
        {
            return Task.FromResult<object>(new { ok = false, problems, notes });
        }

        var ctx = new Ctx(opaque, notes);
        CheckReturnType(parsed.ReturnType, ctx, problems);

        foreach (var p in parsed.Parameters)
        {
            // Synthetic and attribute-driven parameters never become pins.
            if (!p.Wired) continue;
            if (!Classify(p.Type, ctx, Position.Parameter, $"Parameter '{p.Name}'"))
                problems.Add($"Parameter '{p.Name}' has type '{p.Type}' which cannot be a Betta input.");
        }

        return Task.FromResult<object>(new
        {
            ok = problems.Count == 0,
            parsed = new { returnType = parsed.ReturnType, name = parsed.MethodName, parameters = parsed.Parameters },
            problems,
            notes,
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
        /// <summary>False when the runtime injects this parameter instead of exposing an input pin.</summary>
        public bool Wired { get; set; } = true;
        public List<string>? Attributes { get; set; }
    }

    private static ParsedSignature? TryParse(string raw, List<string> problems)
    {
        var s = raw.Trim();
        // Method-level attributes (`[GrasshopperMethod("x")]`, `[return: ...]`) are not
        // needed for acceptance; drop them so the rest parses as a bare signature.
        s = StripLeadingAttributes(s, out _);

        // Strip common access modifiers and return-keyword adornments so the
        // user can paste `public static double Cube(double x)` and have it
        // parse the same as the bare form.
        bool stripped;
        do
        {
            stripped = false;
            foreach (var mod in SignatureModifiers)
            {
                if (s.StartsWith(mod, StringComparison.Ordinal)) { s = s[mod.Length..].TrimStart(); stripped = true; }
            }
        } while (stripped);

        // Drop optional trailing `;`, expression body, or `{ ... }` body.
        // Only a body marker outside parentheses counts: attribute arguments in the
        // parameter list may legitimately contain `{` (`Items = new[] { "a" }`).
        int bodyStart = FindBodyStart(s);
        if (bodyStart >= 0) s = s[..bodyStart].TrimEnd();
        if (s.EndsWith(";", StringComparison.Ordinal)) s = s[..^1].TrimEnd();

        // The parameter list is the LAST top-level (...) group; a tuple return type
        // such as `(int, int) Foo(...)` also contains parentheses, so match backwards.
        int parenClose = s.LastIndexOf(')');
        int parenOpen = parenClose < 0 ? -1 : FindMatchingOpenParen(s, parenClose);
        if (parenOpen < 0 || parenClose < parenOpen)
        {
            problems.Add("Signature must contain `(` and `)`.");
            return null;
        }

        var beforeParen = s[..parenOpen].TrimEnd();
        // Split returnType + methodName on the last top-level whitespace (so
        // `List<int> Foo` and `(int a, int b) Foo` parse correctly).
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
                var partTrim = StripLeadingAttributes(part.Trim(), out var attrs);
                // strip parameter modifiers
                bool again;
                do
                {
                    again = false;
                    foreach (var mod in ParameterModifiers)
                    {
                        if (partTrim.StartsWith(mod, StringComparison.Ordinal)) { partTrim = partTrim[mod.Length..].TrimStart(); again = true; }
                    }
                } while (again);
                // Drop default value: `int x = 0` -> `int x`.
                int eq = partTrim.IndexOf('=');
                if (eq >= 0) partTrim = partTrim[..eq].TrimEnd();

                var p = SplitOnTopLevelLastSpace(partTrim);
                if (p is null)
                {
                    problems.Add($"Could not parse parameter '{partTrim}'.");
                    continue;
                }
                var type = p.Value.left;
                paramList.Add(new ParsedParam
                {
                    Type = type,
                    Name = p.Value.right,
                    Wired = !IsSyntheticType(type) && !attrs.Any(PinlessAttributes.Contains),
                    Attributes = attrs.Count == 0 ? null : attrs,
                });
            }
        }

        return new ParsedSignature
        {
            ReturnType = returnType,
            MethodName = methodName,
            Parameters = paramList,
        };
    }

    /// <summary>
    /// Remove leading C# attribute sections (<c>[A("x")] [B, C]</c>) and return the
    /// remainder; attribute names (without namespace / "Attribute" suffix) are
    /// collected. Brackets inside string literals are respected.
    /// </summary>
    private static string StripLeadingAttributes(string s, out List<string> names)
    {
        names = new List<string>();
        s = s.TrimStart();
        while (s.StartsWith("[", StringComparison.Ordinal))
        {
            int depth = 0, end = -1;
            bool inString = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == '[') depth++;
                else if (c == ']' && --depth == 0) { end = i; break; }
            }
            if (end < 0) break; // unbalanced: leave it for the type parser to complain about

            var body = s.Substring(1, end - 1).Trim();
            // `[return: Foo]` / `[param: Foo]` target prefix.
            int colon = body.IndexOf(':');
            int paren = body.IndexOf('(');
            if (colon >= 0 && (paren < 0 || colon < paren)) body = body[(colon + 1)..].Trim();
            foreach (var attr in SplitOnTopLevelCommas(body))
            {
                var a = attr.Trim();
                int p = a.IndexOf('(');
                if (p >= 0) a = a[..p].Trim();
                int dot = a.LastIndexOf('.');
                if (dot >= 0) a = a[(dot + 1)..];
                if (a.EndsWith("Attribute", StringComparison.Ordinal) && a.Length > "Attribute".Length)
                    a = a[..^"Attribute".Length];
                if (a.Length > 0) names.Add(a);
            }
            s = s[(end + 1)..].TrimStart();
        }
        return s;
    }

    private static int FindBodyStart(string s)
    {
        int depth = 0;
        bool inString = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inString) { if (c == '\\') i++; else if (c == '"') inString = false; continue; }
            if (c == '"') inString = true;
            else if (c == '(' || c == '[') depth++;
            else if (c == ')' || c == ']') depth--;
            else if (depth == 0 && (c == '{' || (c == '=' && i + 1 < s.Length && s[i + 1] == '>'))) return i;
        }
        return -1;
    }

    private static int FindMatchingOpenParen(string s, int close)
    {
        int depth = 0;
        for (int i = close; i >= 0; i--)
        {
            if (s[i] == ')') depth++;
            else if (s[i] == '(' && --depth == 0) return i;
        }
        return -1;
    }

    // Depth tracking shared by the splitters: <>, (), [] nest, and string literals are opaque.
    private static (string left, string right)? SplitOnTopLevelLastSpace(string s)
    {
        int depth = 0, lastSpace = -1;
        bool inString = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inString) { if (c == '\\') i++; else if (c == '"') inString = false; continue; }
            switch (c)
            {
                case '"': inString = true; break;
                case '<': case '(': case '[': depth++; break;
                case '>': case ')': case ']': depth--; break;
                case ' ' when depth == 0: lastSpace = i; break;
            }
        }
        if (lastSpace < 0) return null;
        return (s[..lastSpace].TrimEnd(), s[(lastSpace + 1)..].TrimStart());
    }

    private static IEnumerable<string> SplitOnTopLevelCommas(string s)
    {
        int depth = 0, start = 0;
        bool inString = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inString) { if (c == '\\') i++; else if (c == '"') inString = false; continue; }
            switch (c)
            {
                case '"': inString = true; break;
                case '<': case '(': case '[': depth++; break;
                case '>': case ')': case ']': depth--; break;
                case ',' when depth == 0:
                    yield return s.Substring(start, i - start);
                    start = i + 1; break;
            }
        }
        yield return s.Substring(start);
    }

    // --- type acceptance ---

    private enum Position { Parameter, ReturnTop, Element }

    private sealed class Ctx
    {
        public Ctx(HashSet<string> opaque, List<string> notes) { Opaque = opaque; Notes = notes; }
        public HashSet<string> Opaque { get; }
        public List<string> Notes { get; }
        public void Note(string text) { if (!Notes.Contains(text)) Notes.Add(text); }
    }

    private static bool IsSyntheticType(string typeText)
    {
        var t = typeText.Trim();
        if (StripNamespace(t) is "CancellationToken" or "IBettaReport") return true;
        return TryGetGeneric(t, out var name, out var args) && name == "IProgress" && args.Count == 1;
    }

    private static void CheckReturnType(string typeText, Ctx c, List<string> problems)
    {
        var t = typeText.Trim();
        if (t.Length == 0) { problems.Add("Empty return type."); return; }

        // Async / streaming wrappers (BettaComponent.IsTaskType / IsObservableType).
        for (int guard = 0; guard < 4; guard++)
        {
            var bare = StripNamespace(t);
            if (bare is "Task" or "ValueTask") return; // no result value
            if (TryGetGeneric(t, out var name, out var args) && args.Count == 1
                && name is "Task" or "ValueTask" or "IObservable")
            {
                t = args[0].Trim();
                continue;
            }
            break;
        }

        if (t == "void") return;
        if (!Classify(t, c, Position.ReturnTop, "Return type"))
            problems.Add($"Return type '{typeText}' is not usable as a Betta output.");
    }

    /// <summary>
    /// Returns false only when the runtime genuinely cannot use the type (empty,
    /// void as a value, ill-formed tuple). Degradations add a note and return true.
    /// </summary>
    private static bool Classify(string typeText, Ctx c, Position pos, string subject)
    {
        var t = typeText.Trim();
        if (t.Length == 0) return false;
        if (t == "void") return pos == Position.ReturnTop;

        // Nullable<T> / T? — the runtime coerces through the underlying type.
        if (t.EndsWith("?", StringComparison.Ordinal)) return Classify(t[..^1], c, pos, subject);

        if (c.Opaque.Contains(t) || c.Opaque.Contains(StripNamespace(t))) return true;
        if (KnownTypeNames.Contains(t) || KnownTypeNames.Contains(StripNamespace(t))) return true;
        if (t.StartsWith("Rhino.Geometry.", StringComparison.Ordinal)) return true;

        if (ImageTypeNames.Contains(t))
        {
            c.Note($"{subject}: Bitmap/Image flows as a single generic-object wire (consumers cast it). Return Betta.Preview.BettaImage for a typed, previewable image.");
            return true;
        }

        // Arrays: `T[]`, `params T[]`.
        if (t.EndsWith("]", StringComparison.Ordinal))
        {
            int open = t.LastIndexOf('[');
            if (open > 0)
            {
                if (pos == Position.ReturnTop)
                    c.Note($"{subject}: an array return is not a list output; it is treated as a class and exploded into Array's own property outputs. Return List<T> instead.");
                return Classify(t[..open], c, Position.Element, subject);
            }
        }

        // Tuple shorthand: (double Sum, double Average)
        if (t.StartsWith("(", StringComparison.Ordinal) && t.EndsWith(")", StringComparison.Ordinal))
        {
            var elems = SplitOnTopLevelCommas(t[1..^1])
                .Select(x => x.Trim())
                .Select(x => { var sp = SplitOnTopLevelLastSpace(x); return sp.HasValue ? sp.Value.left : x; })
                .ToList();
            if (elems.Count < 2) return false;
            return ClassifyTuple(elems, c, pos, subject);
        }

        if (TryGetGeneric(t, out var name, out var args))
        {
            switch (name)
            {
                case "List":
                    return args.Count != 1 || Classify(args[0], c, Position.Element, subject);

                case "GH_Structure":
                    return true; // tree input / output

                case "ValueTuple":
                case "Tuple":
                    if (args.Count > 8) return false; // ill-formed: TRest must nest
                    return ClassifyTuple(args, c, pos, subject);
            }

            if (DegradedCollectionNames.Contains(name) && args.Count == 1)
            {
                c.Note(pos == Position.Parameter
                    ? $"{subject}: {name}<T> is not a list input; it becomes a generic-object input. Use List<T> or T[] for list access."
                    : $"{subject}: {name}<T> is not a list output; it ships as a single generic output. Return List<T> for a list output.");
                return Classify(args[0], c, Position.Element, subject);
            }

            c.Note(UnknownTypeNote(t, pos, subject));
            return true;
        }

        c.Note(UnknownTypeNote(t, pos, subject));
        return true;
    }

    private static bool ClassifyTuple(List<string> elems, Ctx c, Position pos, string subject)
    {
        if (pos == Position.Parameter)
            c.Note($"{subject}: a tuple parameter is not split into pins; it becomes a single generic-object input.");
        bool ok = true;
        foreach (var e in elems)
            ok &= Classify(e, c, Position.Element, subject);
        return ok;
    }

    private static string UnknownTypeNote(string t, Position pos, string subject)
    {
        switch (pos)
        {
            case Position.Parameter:
                return $"{subject}: '{t}' is not a Betta-known type. It becomes a generic-object input (an enum surfaces as an Integer input and is coerced back). List it in opaqueTypes / mark it [GrasshopperOpaque] for a typed wire.";
            case Position.ReturnTop:
                var bare = StripNamespace(t);
                if (bare.Length > 1 && bare[0] == 'I' && char.IsUpper(bare[1]))
                    return $"{subject}: '{t}' looks like an interface; it ships as a single generic output. Mark it [GrasshopperOpaque] for a typed wire.";
                return $"{subject}: '{t}' is not a Betta-known type. If it is a class, it is exploded into up to 10 property outputs (simple, opaque and List<> properties only; a single generic output if none qualify); an enum is a plain Integer output. Mark it [GrasshopperOpaque] / list it in opaqueTypes to ship it as one typed wire.";
            default:
                return $"{subject}: element type '{t}' is not a Betta-known type; it flows as a generic-object wire.";
        }
    }

    /// <summary>Split <c>Name&lt;A, B&gt;</c> into the bare (namespace-less) name and top-level args.</summary>
    private static bool TryGetGeneric(string text, out string name, out List<string> args)
    {
        name = string.Empty;
        args = new List<string>();
        int lt = text.IndexOf('<');
        if (lt <= 0 || !text.EndsWith(">", StringComparison.Ordinal)) return false;
        name = StripNamespace(text[..lt]);
        args = SplitOnTopLevelCommas(text.Substring(lt + 1, text.Length - lt - 2)).Select(a => a.Trim()).ToList();
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
