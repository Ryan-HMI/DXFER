using System.Globalization;
using System.Net.Http.Headers;
using System.Text;

namespace DXFER.Core.Sync;

public sealed class SyncCallbackClient
{
    private const string DefaultCallbackPath = "/api/dxfer/edit-callback";

    private readonly HttpClient _httpClient;

    public SyncCallbackClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task PostSaveAsync(
        SyncEditLaunchOptions launchOptions,
        SyncSavePackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launchOptions);
        ArgumentNullException.ThrowIfNull(package);

        if (!launchOptions.IsCallbackConfigured)
        {
            throw new InvalidOperationException("Sync callback save requires syncBaseUrl, artifactId, jobId, and editToken.");
        }

        using var content = CreateMultipartContent(package);
        using var response = await _httpClient.PostAsync(
            BuildCallbackUri(launchOptions.SyncBaseUrl!),
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private MultipartFormDataContent CreateMultipartContent(SyncSavePackage package)
    {
        var content = new MultipartFormDataContent();
        AddString(content, "artifactId", package.ArtifactId);
        AddString(content, "jobId", package.JobId);
        AddString(content, "editToken", package.EditToken);
        AddString(content, "boundingWidth", Format(package.BoundingWidth));
        AddString(content, "boundingHeight", Format(package.BoundingHeight));
        AddString(content, "rotationDegrees", Format(package.RotationDegrees));
        AddString(content, "originShiftX", Format(package.OriginShiftX));
        AddString(content, "originShiftY", Format(package.OriginShiftY));
        AddString(content, "grainDirection", package.GrainDirection.ToString());
        AddString(content, "manualOverride", package.ManualOverride ? "true" : "false");

        var dxfContent = new StringContent(package.NormalizedDxfContent, Encoding.UTF8);
        dxfContent.Headers.ContentType = new MediaTypeHeaderValue("application/dxf");
        content.Add(dxfContent, "normalizedDxf", package.NormalizedDxfFileName);

        var metadataContent = new StringContent(package.MetadataJson, Encoding.UTF8);
        metadataContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Add(metadataContent, "metadataJson", "dxfer.json");
        return content;
    }

    private Uri BuildCallbackUri(string syncBaseUrl)
    {
        var baseText = syncBaseUrl.EndsWith("/", StringComparison.Ordinal)
            ? syncBaseUrl
            : $"{syncBaseUrl}/";
        var path = DefaultCallbackPath.TrimStart('/');
        return new Uri(new Uri(baseText, UriKind.Absolute), path);
    }

    private static void AddString(MultipartFormDataContent content, string name, string value) =>
        content.Add(new StringContent(value, Encoding.UTF8), name);

    private static string Format(double value) =>
        value.ToString("0.##########", CultureInfo.InvariantCulture);
}
