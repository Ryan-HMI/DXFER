namespace DXFER.Core.Sync;

public sealed record SyncEditLaunchOptions(
    string? SyncBaseUrl,
    string? ArtifactId,
    string? JobId,
    string? EditToken,
    string? InputPath,
    string? DownloadUrl,
    string? ReturnUrl,
    string? JobFolder)
{
    public static SyncEditLaunchOptions Empty { get; } = new(
        SyncBaseUrl: null,
        ArtifactId: null,
        JobId: null,
        EditToken: null,
        InputPath: null,
        DownloadUrl: null,
        ReturnUrl: null,
        JobFolder: null);

    public bool IsCallbackConfigured =>
        !string.IsNullOrWhiteSpace(SyncBaseUrl)
        && !string.IsNullOrWhiteSpace(ArtifactId)
        && !string.IsNullOrWhiteSpace(JobId)
        && !string.IsNullOrWhiteSpace(EditToken);

    public bool HasInput =>
        !string.IsNullOrWhiteSpace(InputPath)
        || !string.IsNullOrWhiteSpace(DownloadUrl);
}
