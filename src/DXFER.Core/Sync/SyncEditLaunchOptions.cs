namespace DXFER.Core.Sync;

public sealed record SyncEditLaunchOptions(
    string? SyncBaseUrl,
    string? ArtifactId,
    string? JobId,
    string? EditToken,
    string? InputPath,
    string? DownloadUrl,
    string? ReturnUrl,
    string? JobFolder,
    bool AutoNormalize = true,
    bool SaveToSync = true)
{
    public static SyncEditLaunchOptions Empty { get; } = new(
        SyncBaseUrl: null,
        ArtifactId: null,
        JobId: null,
        EditToken: null,
        InputPath: null,
        DownloadUrl: null,
        ReturnUrl: null,
        JobFolder: null,
        AutoNormalize: true);

    // Presence only: Sync still validates the signed download and callback tokens.
    public bool HasLaunchCredentials =>
        !string.IsNullOrWhiteSpace(SyncBaseUrl)
        && !string.IsNullOrWhiteSpace(ArtifactId)
        && !string.IsNullOrWhiteSpace(JobId)
        && !string.IsNullOrWhiteSpace(EditToken);

    public bool IsCallbackConfigured => SaveToSync && HasLaunchCredentials;

    public bool HasInput =>
        !string.IsNullOrWhiteSpace(InputPath)
        || !string.IsNullOrWhiteSpace(DownloadUrl);
}
