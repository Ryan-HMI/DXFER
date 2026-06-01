namespace DXFER.Core.Sync;

public static class SyncLaunchOptionsParser
{
    public static SyncEditLaunchOptions ParseQueryString(string? queryString)
    {
        if (string.IsNullOrWhiteSpace(queryString))
        {
            return SyncEditLaunchOptions.Empty;
        }

        var query = queryString.StartsWith("?", StringComparison.Ordinal)
            ? queryString[1..]
            : queryString;
        if (string.IsNullOrWhiteSpace(query))
        {
            return SyncEditLaunchOptions.Empty;
        }

        var values = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var separator = part.IndexOf('=', StringComparison.Ordinal);
                var key = separator >= 0 ? part[..separator] : part;
                var value = separator >= 0 ? part[(separator + 1)..] : string.Empty;
                return new KeyValuePair<string, string?>(
                    Uri.UnescapeDataString(key.Replace("+", " ", StringComparison.Ordinal)),
                    Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal)));
            });

        return Parse(values);
    }

    public static SyncEditLaunchOptions Parse(IEnumerable<KeyValuePair<string, string?>> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var lookup = values
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value,
                StringComparer.OrdinalIgnoreCase);

        return new SyncEditLaunchOptions(
            Read(lookup, "syncBaseUrl"),
            Read(lookup, "artifactId"),
            Read(lookup, "jobId"),
            Read(lookup, "editToken"),
            Read(lookup, "inputPath"),
            Read(lookup, "downloadUrl"),
            Read(lookup, "returnUrl"),
            Read(lookup, "jobFolder"),
            ReadBool(lookup, "autoNormalize", defaultValue: true));
    }

    private static string? Read(IReadOnlyDictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static bool ReadBool(IReadOnlyDictionary<string, string?> values, string key, bool defaultValue)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "y" => true,
            "false" or "0" or "no" or "n" => false,
            _ => defaultValue
        };
    }
}
