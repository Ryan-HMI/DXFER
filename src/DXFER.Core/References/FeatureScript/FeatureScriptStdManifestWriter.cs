using System.Text.Json;

namespace DXFER.Core.References.FeatureScript;

public static class FeatureScriptStdManifestWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Write(FeatureScriptStdIndex index, DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(index);

        var manifest = new FeatureScriptStdManifest(
            generatedAtUtc.ToUniversalTime(),
            index.SourceRoot,
            index.LicenseRelativePath,
            index.ModuleCount,
            index.Modules);

        return JsonSerializer.Serialize(manifest, JsonOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private sealed record FeatureScriptStdManifest(
        DateTimeOffset GeneratedAtUtc,
        string SourceRoot,
        string? LicenseRelativePath,
        int ModuleCount,
        IReadOnlyList<FeatureScriptStdModule> Modules);
}
