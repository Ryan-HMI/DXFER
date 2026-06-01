using System.Net;
using System.Net.Http;
using DXFER.Core.Sync;
using FluentAssertions;

namespace DXFER.Core.Tests.Sync;

public sealed class SyncContractTests
{
    [Fact]
    public void LaunchParserReadsSyncQueryContract()
    {
        var query = new Dictionary<string, string?>
        {
            ["syncBaseUrl"] = "https://sync.local",
            ["artifactId"] = "artifact-1",
            ["jobId"] = "job-1",
            ["editToken"] = "token",
            ["inputPath"] = "C:/temp/input.dxf",
            ["returnUrl"] = "https://sync.local/return",
            ["jobFolder"] = "C:/temp/job",
            ["autoNormalize"] = "false"
        };

        var options = SyncLaunchOptionsParser.Parse(query);

        options.SyncBaseUrl.Should().Be("https://sync.local");
        options.ArtifactId.Should().Be("artifact-1");
        options.JobId.Should().Be("job-1");
        options.EditToken.Should().Be("token");
        options.InputPath.Should().Be("C:/temp/input.dxf");
        options.JobFolder.Should().Be("C:/temp/job");
        options.AutoNormalize.Should().BeFalse();
        options.IsCallbackConfigured.Should().BeTrue();
    }

    [Fact]
    public void LaunchParserLeavesCallbackDisabledWithoutRequiredSyncFields()
    {
        var options = SyncLaunchOptionsParser.Parse(new Dictionary<string, string?>
        {
            ["inputPath"] = "C:/temp/input.dxf"
        });

        options.IsCallbackConfigured.Should().BeFalse();
        options.InputPath.Should().Be("C:/temp/input.dxf");
    }

    [Fact]
    public void LaunchParserReadsEncodedQueryString()
    {
        var options = SyncLaunchOptionsParser.ParseQueryString(
            "?syncBaseUrl=https%3A%2F%2Fsync.local&artifactId=a+1&jobId=j1&editToken=t1&downloadUrl=https%3A%2F%2Fsync.local%2Ffile.dxf");

        options.SyncBaseUrl.Should().Be("https://sync.local");
        options.ArtifactId.Should().Be("a 1");
        options.DownloadUrl.Should().Be("https://sync.local/file.dxf");
        options.AutoNormalize.Should().BeTrue();
        options.IsCallbackConfigured.Should().BeTrue();
    }

    [Fact]
    public void SavePackageKeepsSyncAsSourceOfTruth()
    {
        var package = CreatePackage(manualOverride: true);

        package.GrainDirection.Should().Be(GrainDirectionOption.X);
        package.ManualOverride.Should().BeTrue();
        package.NormalizedDxfContent.Should().Contain("EOF");
    }

    [Fact]
    public async Task CallbackClientPostsMultipartPayloadToSync()
    {
        using var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new SyncCallbackClient(httpClient);
        var launch = new SyncEditLaunchOptions(
            SyncBaseUrl: "https://sync.local/app",
            ArtifactId: "artifact-1",
            JobId: "job-1",
            EditToken: "token",
            InputPath: "C:/temp/input.dxf",
            DownloadUrl: null,
            ReturnUrl: "https://sync.local/return",
            JobFolder: null);

        await client.PostSaveAsync(launch, CreatePackage(manualOverride: false));

        handler.RequestUri.Should().Be(new Uri("https://sync.local/app/api/dxfer/edit-callback"));
        handler.ContentType.Should().StartWith("multipart/form-data");
        handler.Body.Should().Contain("name=artifactId");
        handler.Body.Should().Contain("artifact-1");
        handler.Body.Should().Contain("name=normalizedDxf");
        handler.Body.Should().Contain("normalized.dxf");
        handler.Body.Should().NotContain("name=metadataJson");
        handler.Body.Should().NotContain("dxfer.json");
        handler.Body.Should().Contain("name=boundingWidth");
        handler.Body.Should().Contain("20");
        handler.Body.Should().Contain("name=grainDirection");
        handler.Body.Should().Contain("X");
        handler.Body.Should().Contain("name=manualOverride");
        handler.Body.Should().Contain("false");
    }

    private static SyncSavePackage CreatePackage(bool manualOverride) =>
        new(
            ArtifactId: "artifact-1",
            JobId: "job-1",
            EditToken: "token",
            NormalizedDxfFileName: "normalized.dxf",
            NormalizedDxfContent: "0\nEOF\n",
            BoundingWidth: 20,
            BoundingHeight: 10,
            RotationDegrees: -30,
            OriginShiftX: -4,
            OriginShiftY: 8,
            GrainDirection: GrainDirectionOption.X,
            ManualOverride: manualOverride);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? ContentType { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ContentType = request.Content?.Headers.ContentType?.ToString();
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
