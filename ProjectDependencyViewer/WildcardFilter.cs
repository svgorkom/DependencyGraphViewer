using System.Text.RegularExpressions;

namespace ProjectDependencyViewer;

/// <summary>
/// Applies wildcard-based string filtering with regex caching.
/// Supports <c>*</c> (any sequence) and <c>?</c> (single character) wildcards.
/// </summary>
internal static class WildcardFilter
{
    private static readonly Dictionary<string, Regex> RegexCache = new(StringComparer.Ordinal);

    public static List<string> Apply(List<string> items, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return items;

        var regex = GetOrCreateRegex(filter);
        return items.Where(item => regex.IsMatch(item)).ToList();
    }

    public static List<string> Exclude(List<string> items, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return items;

        var patterns = filter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (patterns.Length == 0)
            return items;

        var regexes = patterns.Select(GetOrCreateRegex).ToList();
        return items.Where(item => !regexes.Any(r => r.IsMatch(item))).ToList();
    }

    private static Regex GetOrCreateRegex(string filter)
    {
        if (RegexCache.TryGetValue(filter, out var cached))
            return cached;

        if (RegexCache.Count > 32)
            RegexCache.Clear();

        var pattern = "^" + Regex.Escape(filter).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        RegexCache[filter] = regex;
        return regex;
    }
}
