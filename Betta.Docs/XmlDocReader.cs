// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Xml;

namespace Betta.Docs;

/// <summary>
/// Reads C# XML documentation file (foo.xml beside foo.dll) and extracts
/// &lt;summary&gt; for methods and &lt;param&gt; for parameters. Falls back to
/// an empty reader if no XML is present — never throws.
/// </summary>
internal sealed class XmlDocReader
{
    private readonly Dictionary<string, MethodDoc> _docs = new();

    public static XmlDocReader LoadAlongside(string dllPath)
    {
        var reader = new XmlDocReader();
        var xmlPath = Path.ChangeExtension(dllPath, ".xml");
        if (!File.Exists(xmlPath)) return reader;

        try
        {
            var doc = new XmlDocument();
            doc.Load(xmlPath);
            var members = doc.SelectNodes("/doc/members/member");
            if (members is null) return reader;
            foreach (XmlNode m in members)
            {
                var name = m.Attributes?["name"]?.Value;
                if (string.IsNullOrEmpty(name) || !name!.StartsWith("M:")) continue;
                var summary = Clean(m.SelectSingleNode("summary")?.InnerText);
                var paramDocs = new Dictionary<string, string>(StringComparer.Ordinal);
                var paramNodes = m.SelectNodes("param");
                if (paramNodes is not null)
                {
                    foreach (XmlNode p in paramNodes)
                    {
                        var pn = p.Attributes?["name"]?.Value;
                        if (string.IsNullOrEmpty(pn)) continue;
                        paramDocs[pn!] = Clean(p.InnerText);
                    }
                }
                reader._docs[name!] = new MethodDoc(summary, paramDocs);
            }
        }
        catch
        {
            // Best-effort. XML doc bugs shouldn't break documentation generation.
        }
        return reader;
    }

    public string? GetMethodSummary(MethodInfo method)
    {
        var key = BuildMemberId(method);
        if (key is not null && _docs.TryGetValue(key, out var d) && !string.IsNullOrWhiteSpace(d.Summary))
            return d.Summary;
        return null;
    }

    public string? GetParamSummary(ParameterInfo p)
    {
        if (p.Member is not MethodInfo m) return null;
        var key = BuildMemberId(m);
        if (key is null || !_docs.TryGetValue(key, out var d)) return null;
        if (p.Name is not null && d.Params.TryGetValue(p.Name, out var s) && !string.IsNullOrWhiteSpace(s))
            return s;
        return null;
    }

    private static string Clean(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? "" : System.Text.RegularExpressions.Regex.Replace(raw.Trim(), @"\s+", " ");

    /// <summary>
    /// Build the C# XML doc member id for a method, e.g.
    ///   M:Betta.Strings.IStringCollection.Concat(System.String,System.String,System.String)
    /// Best-effort: nested generic param formatting is not exhaustively handled
    /// because Betta plugin methods rarely use generics on parameters.
    /// </summary>
    private static string? BuildMemberId(MethodInfo m)
    {
        if (m.DeclaringType?.FullName is null) return null;
        var typeFull = m.DeclaringType.FullName.Replace('+', '.');
        var ps = m.GetParameters();
        if (ps.Length == 0) return $"M:{typeFull}.{m.Name}";

        var args = string.Join(",", ps.Select(p => FormatXmlParamType(p.ParameterType)));
        return $"M:{typeFull}.{m.Name}({args})";
    }

    private static string FormatXmlParamType(Type t)
    {
        if (t.IsByRef) return FormatXmlParamType(t.GetElementType()!) + "@";
        if (t.IsArray) return FormatXmlParamType(t.GetElementType()!) + "[]";
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition().FullName ?? t.GetGenericTypeDefinition().Name;
            // Strip the `N arity
            var tick = def.IndexOf('`');
            var baseName = tick >= 0 ? def.Substring(0, tick) : def;
            var args = string.Join(",", t.GetGenericArguments().Select(FormatXmlParamType));
            return baseName + "{" + args + "}";
        }
        return t.FullName ?? t.Name;
    }

    private sealed record MethodDoc(string Summary, Dictionary<string, string> Params);
}
