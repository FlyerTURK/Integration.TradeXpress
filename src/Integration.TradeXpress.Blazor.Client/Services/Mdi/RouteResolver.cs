using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Services.Mdi;

public sealed record RouteMatch(Type PageType, Dictionary<string, object> Parameters);

/// <summary>
/// URL → sayfa tipi + parametre çözümleyici. Blazor Router'ı DynamicComponent altında devre dışı
/// kaldığından (route param + [SupplyParameterFromQuery] yalnız Router tarafından doldurulur),
/// route segment'lerini ve query string'i kendimiz parse edip C# PROPERTY adıyla key'leriz.
/// </summary>
public sealed class RouteResolver
{
    private sealed class Entry
    {
        public required string[] Segments;
        public required Type PageType;
        public required Dictionary<string, PropertyInfo> ParamProps; // ad (ci) → [Parameter] prop
        public required Dictionary<string, PropertyInfo> QueryAlias;  // query alias (ci) → prop
    }

    private readonly List<Entry> _entries = new();

    public RouteResolver()
    {
        foreach (var type in SafeGetTypes(typeof(RouteResolver).Assembly))
        {
            if (!typeof(IComponent).IsAssignableFrom(type)) continue;

            var routes = type.GetCustomAttributes<RouteAttribute>().ToList();
            if (routes.Count == 0) continue;

            var paramProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<ParameterAttribute>() != null || 
                            p.GetCustomAttribute<SupplyParameterFromQueryAttribute>() != null)
                .ToList();

            var paramMap = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paramProps) paramMap[p.Name] = p;

            var queryMap = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paramProps)
            {
                var q = p.GetCustomAttribute<SupplyParameterFromQueryAttribute>();
                if (q == null) continue;
                var alias = string.IsNullOrEmpty(q.Name) ? p.Name : q.Name!;
                queryMap[alias] = p;
            }

            foreach (var r in routes)
                _entries.Add(new Entry
                {
                    Segments = SplitPath(r.Template),
                    PageType = type,
                    ParamProps = paramMap,
                    QueryAlias = queryMap,
                });
        }
    }

    public bool IsKnownPage(string url) => Match(url) != null;

    public RouteMatch? Match(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var (path, query) = SplitUrl(url);
        var pathSegs = SplitPath(path);

        foreach (var e in _entries)
        {
            if (!TryMatchSegments(e, pathSegs, out var routeValues)) continue;

            var pars = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (var (name, raw) in routeValues)
                if (e.ParamProps.TryGetValue(name, out var prop))
                {
                    var val = Convert(raw, prop.PropertyType);
                    if (val != null) pars[prop.Name] = val;
                }

            if (!string.IsNullOrEmpty(query))
                foreach (var (k, v) in ParseQuery(query))
                    if (e.QueryAlias.TryGetValue(k, out var prop))
                    {
                        var val = Convert(v, prop.PropertyType);
                        if (val != null) pars[prop.Name] = val;
                    }

            return new RouteMatch(e.PageType, pars);
        }

        return null;
    }

    private static bool TryMatchSegments(Entry e, string[] pathSegs, out List<(string Name, string Value)> values)
    {
        values = new();
        if (e.Segments.Length != pathSegs.Length) return false;

        for (int i = 0; i < e.Segments.Length; i++)
        {
            var t = e.Segments[i];
            if (t.Length > 1 && t[0] == '{' && t[^1] == '}')
            {
                var inner = t[1..^1];
                var name = inner.Split(':')[0].TrimStart('*');
                values.Add((name, pathSegs[i]));
            }
            else if (!string.Equals(t, pathSegs[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private static string[] SplitPath(string s) => s.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static (string path, string query) SplitUrl(string url)
    {
        var q = url.IndexOf('?');
        return q < 0 ? (url, "") : (url[..q], url[(q + 1)..]);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            var key = idx >= 0 ? pair[..idx] : pair;
            var val = idx >= 0 ? pair[(idx + 1)..] : "";
            result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(val);
        }
        return result;
    }

    private static object? Convert(string raw, Type target)
    {
        var t = Nullable.GetUnderlyingType(target) ?? target;
        if (t == typeof(string)) return raw;
        if (t == typeof(Guid)) return Guid.TryParse(raw, out var g) ? g : null;
        if (t == typeof(int)) return int.TryParse(raw, out var i) ? i : null;
        if (t == typeof(long)) return long.TryParse(raw, out var l) ? l : null;
        if (t == typeof(bool)) return bool.TryParse(raw, out var b) ? b : null;
        if (t.IsEnum) { try { return Enum.Parse(t, raw, true); } catch { return null; } }
        return raw;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
    }
}
