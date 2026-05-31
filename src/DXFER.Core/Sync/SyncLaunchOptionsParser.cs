namespace DXFER.Core.Sync;

public static class SyncLaunchOptionsParser
{
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
            Read(lookup, "jobFolder"));
    }

    private static string? Read(IReadOnlyDictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
}
