using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DXFER.CadIO;
using DXFER.Blazor.Components;
using DXFER.Core.Documents;
using DXFER.Core.IO;
using DXFER.Core.Operations;
using DXFER.Core.Sync;
using DXFER.Web.Components;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = builder.Environment.IsDevelopment();
    });
builder.Services.AddScoped<WorkbenchMenuCommandService>();
builder.Services.AddScoped<ToolHotkeyService>();
builder.Services.AddScoped<HttpClient>();
builder.Services.AddHttpClient<SyncCallbackClient>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/api/dxfer/capabilities", () => Results.Json(new
{
    contractVersion = 1,
    launchParameters = new[]
    {
        "syncBaseUrl",
        "artifactId",
        "jobId",
        "editToken",
        "inputPath",
        "downloadUrl",
        "returnUrl",
        "jobFolder"
    },
    callbackPath = "/api/dxfer/edit-callback",
    normalizeEndpoint = "/api/dxfer/normalize",
    grainDirections = Enum.GetNames<GrainDirectionOption>(),
    sourceOfTruth = "Sync validates token and artifact ownership, stores artifacts, updates metadata, and marks geometry clean."
}));

app.MapPost("/api/dxfer/normalize", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart form data with a DXF file field named 'dxf' or 'file'." });
    }

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("dxf") ?? form.Files.GetFile("file");
    if (file is null)
    {
        return Results.BadRequest(new { error = "Missing DXF file field named 'dxf' or 'file'." });
    }

    const long maxDxfFileSize = 25 * 1024 * 1024;
    if (file.Length > maxDxfFileSize)
    {
        return Results.BadRequest(new { error = "DXF file exceeds the 25 MB limit." });
    }

    await using var stream = file.OpenReadStream();
    using var reader = new StreamReader(stream);
    var sourceText = await reader.ReadToEndAsync();
    var sourceDocument = WithApiMetadata(DxfDocumentReader.Read(sourceText), file.FileName, sourceText);
    if (sourceDocument.Entities.Count == 0)
    {
        return Results.BadRequest(new { error = "No supported DXF entities were found." });
    }

    var normalization = DrawingNormalizationService.AutoNormalize(sourceDocument);
    var normalizedFileName = GetNormalizedDxfFileName(file.FileName);
    var normalizedDocument = WithNormalizedFileName(normalization.NormalizedDocument, normalizedFileName);
    var normalizedDxf = DxfDocumentWriter.Write(normalizedDocument);
    var metadataJson = DxferSidecarWriter.Write(
        normalizedDocument,
        sourceText,
        normalizedDxf);
    var bounds = normalizedDocument.GetBounds();

    return Results.Json(new
    {
        normalizedDxfFileName = normalizedFileName,
        normalizedDxf,
        metadataJson,
        boundingWidth = bounds.Width,
        boundingHeight = bounds.Height,
        rotationDegrees = normalization.RotationDegrees,
        originShiftX = normalization.OriginShiftX,
        originShiftY = normalization.OriginShiftY,
        grainDirection = GrainDirectionOption.None.ToString(),
        manualOverride = false,
        warnings = normalizedDocument.Metadata.Warnings.Select(warning => new
        {
            warning.Code,
            severity = warning.Severity.ToString(),
            warning.Message
        }),
        unsupportedEntityCounts = normalizedDocument.Metadata.UnsupportedEntityCounts
    });
});

app.Run();

static DrawingDocument WithApiMetadata(DrawingDocument document, string fileName, string sourceText)
{
    var metadata = document.Metadata with
    {
        SourceFileName = fileName,
        SourceSha256 = ComputeSha256(sourceText),
        TrustedSource = true
    };

    return new DrawingDocument(
        document.Entities,
        document.Dimensions,
        document.Constraints,
        metadata);
}

static DrawingDocument WithNormalizedFileName(DrawingDocument document, string normalizedFileName)
{
    var metadata = document.Metadata with
    {
        NormalizedFileName = normalizedFileName
    };

    return new DrawingDocument(
        document.Entities,
        document.Dimensions,
        document.Constraints,
        metadata);
}

static string GetNormalizedDxfFileName(string fileName)
{
    var stem = Path.GetFileNameWithoutExtension(fileName);
    if (string.IsNullOrWhiteSpace(stem))
    {
        stem = "normalized";
    }

    return $"{stem}.normalized.dxf";
}

static string ComputeSha256(string content)
{
    using var sha256 = SHA256.Create();
    var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
    return string.Concat(hash.Select(part => part.ToString("x2", CultureInfo.InvariantCulture)));
}
