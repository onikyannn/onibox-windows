using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;

namespace Onibox.Services;

public static class Localization
{
    private const string DefaultCulture = "en-US";
    private static readonly string StringsRoot = Path.Combine(AppContext.BaseDirectory, "Strings");
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Cache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<HashSet<string>> AvailableCultures =
        new(LoadAvailableCultures);

    public static string GetString(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        foreach (var cultureName in EnumerateCultures(CultureInfo.CurrentUICulture))
        {
            var map = GetMapForCulture(cultureName);
            if (map is null)
            {
                continue;
            }

            if (map.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return key;
    }

    private static IReadOnlyDictionary<string, string>? GetMapForCulture(string cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return null;
        }

        var resolved = ResolveCultureName(cultureName);
        return Cache.GetOrAdd(resolved, LoadResourcesForCulture);
    }

    private static IReadOnlyDictionary<string, string> LoadResourcesForCulture(string cultureName)
    {
        var filePath = Path.Combine(StringsRoot, cultureName, "Resources.resw");
        if (!File.Exists(filePath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var document = XDocument.Load(filePath);
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var element in document.Root?.Elements("data") ?? Enumerable.Empty<XElement>())
            {
                var name = element.Attribute("name")?.Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var value = element.Element("value")?.Value ?? string.Empty;
                entries[name] = value;
            }

            return entries;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static IEnumerable<string> EnumerateCultures(CultureInfo culture)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = culture;
        while (!string.IsNullOrWhiteSpace(current.Name))
        {
            if (seen.Add(current.Name))
            {
                yield return current.Name;
            }

            if (current.Equals(CultureInfo.InvariantCulture))
            {
                break;
            }

            current = current.Parent;
        }

        if (seen.Add(DefaultCulture))
        {
            yield return DefaultCulture;
        }
    }

    private static string ResolveCultureName(string cultureName)
    {
        if (AvailableCultures.Value.Contains(cultureName))
        {
            return cultureName;
        }

        if (cultureName.Length == 2)
        {
            var match = AvailableCultures.Value.FirstOrDefault(name =>
                name.StartsWith(cultureName + "-", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return cultureName;
    }

    private static HashSet<string> LoadAvailableCultures()
    {
        if (!Directory.Exists(StringsRoot))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(StringsRoot))
        {
            var name = Path.GetFileName(directory);
            if (!string.IsNullOrWhiteSpace(name))
            {
                cultures.Add(name);
            }
        }

        return cultures;
    }
}
