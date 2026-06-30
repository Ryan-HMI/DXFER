using System.Globalization;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using DXFER.CadIO;
using DXFER.Blazor.Components;
using DXFER.Core.Documents;
using DXFER.Core.Operations;
using DXFER.Core.Sync;
using DXFER.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.HttpOverrides;

LoadDotEnvFromCurrentDirectory();
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
var googleClientId = ResolveGoogleSetting(builder.Configuration, "ClientId", "GOOGLE_CLIENT_ID");
var googleClientSecret = ResolveGoogleSetting(builder.Configuration, "ClientSecret", "GOOGLE_CLIENT_SECRET");
var googleCallbackPath = ResolveGoogleSetting(builder.Configuration, "CallbackPath", "GOOGLE_CALLBACK_PATH");
var hasGoogleAuth = !string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret);
var allowedGoogleDomains = ResolveAllowedGoogleDomains(builder.Configuration);
var dxferApiKey = ResolveDxferApiKey(builder.Configuration);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = builder.Environment.IsDevelopment();
    });
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<WorkbenchMenuCommandService>();
builder.Services.AddScoped<ToolHotkeyService>();
builder.Services.AddScoped<HttpClient>();
builder.Services.AddHttpClient<SyncCallbackClient>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedHost |
        ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var authBuilder = builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "dxfer.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/auth/login";
        options.LogoutPath = "/auth/logout";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnRedirectToLogin = context =>
        {
            if (IsApiRequest(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

if (hasGoogleAuth)
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId!;
        options.ClientSecret = googleClientSecret!;
        if (!string.IsNullOrWhiteSpace(googleCallbackPath))
        {
            options.CallbackPath = googleCallbackPath!;
        }

        options.Events.OnRedirectToAuthorizationEndpoint = context =>
        {
            var separator = context.RedirectUri.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            context.Response.Redirect(context.RedirectUri + separator + "prompt=select_account");
            return Task.CompletedTask;
        };
        options.Events.OnCreatingTicket = context =>
        {
            var email = context.Principal?.FindFirstValue(ClaimTypes.Email);
            if (!IsAllowedGoogleEmail(email, allowedGoogleDomains))
            {
                context.Fail("DXFER access is restricted to approved Google Workspace accounts.");
            }

            return Task.CompletedTask;
        };
    });
}

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAuthentication();
app.Use(async (context, next) => await RequireDxferAccessAsync(context, next, hasGoogleAuth, dxferApiKey));
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/auth/login", (HttpContext context) =>
{
    if (!hasGoogleAuth)
    {
        return Results.Problem(
            "Google authentication is not configured. Set Authentication:Google:ClientId and Authentication:Google:ClientSecret, or GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var returnUrl = context.Request.Query["returnUrl"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith("/", StringComparison.Ordinal))
    {
        returnUrl = "/";
    }

    return Results.Challenge(
        new AuthenticationProperties { RedirectUri = returnUrl },
        new[] { GoogleDefaults.AuthenticationScheme });
}).AllowAnonymous();

app.MapGet("/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
}).AllowAnonymous();

app.MapGet("/auth/status", (HttpContext context) => Results.Json(new
{
    authenticated = context.User.Identity?.IsAuthenticated == true,
    email = context.User.FindFirstValue(ClaimTypes.Email),
    syncLaunchSession = context.User.HasClaim("dxfer_access", "sync-launch"),
    googleAuthConfigured = hasGoogleAuth,
    serverApiKeyConfigured = !string.IsNullOrWhiteSpace(dxferApiKey),
    allowedGoogleDomains
})).AllowAnonymous();

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
    syncImportEndpoint = "/api/dxfer/normalize",
    syncExportCallbackPath = "/api/dxfer/edit-callback",
    manualFileControls = new[]
    {
        "Open local DXF/DWG",
        "Download DXF"
    },
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
    var bounds = normalizedDocument.GetBounds();

    return Results.Json(new
    {
        normalizedDxfFileName = normalizedFileName,
        normalizedDxf,
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

static async Task RequireDxferAccessAsync(
    HttpContext context,
    Func<Task> next,
    bool hasGoogleAuth,
    string? dxferApiKey)
{
    var path = context.Request.Path;
    if (IsAnonymousPath(path))
    {
        await next();
        return;
    }

    if (HasValidDxferApiKey(context.Request, dxferApiKey))
    {
        await next();
        return;
    }

    if (context.User.Identity?.IsAuthenticated == true)
    {
        await next();
        return;
    }

    if (IsSyncLaunchRequest(context.Request))
    {
        await SignInSyncLaunchSessionAsync(context);
        await next();
        return;
    }

    if (IsApiRequest(path))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "DXFER authentication is required.",
            googleAuthConfigured = hasGoogleAuth,
            serverApiKeyConfigured = !string.IsNullOrWhiteSpace(dxferApiKey)
        });
        return;
    }

    if (!hasGoogleAuth)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsync(
            "Google authentication is not configured. Set Authentication:Google:ClientId and Authentication:Google:ClientSecret, or GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET.");
        return;
    }

    await context.ChallengeAsync(
        GoogleDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = context.Request.GetEncodedPathAndQuery() });
}

static async Task SignInSyncLaunchSessionAsync(HttpContext context)
{
    var identity = new ClaimsIdentity(
        new[]
        {
            new Claim(ClaimTypes.Name, "Sync launch"),
            new Claim("dxfer_access", "sync-launch")
        },
        "SyncLaunch");
    var principal = new ClaimsPrincipal(identity);
    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2)
        });
    context.User = principal;
}

static bool IsAnonymousPath(PathString path)
{
    if (path.StartsWithSegments("/auth", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/signin-google", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/dxfer/capabilities", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/_framework", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/_content", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/css", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/js", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/lib", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/favicon", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    var value = path.Value ?? string.Empty;
    return value.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".woff", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase);
}

static bool IsApiRequest(PathString path) =>
    path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

static bool HasValidDxferApiKey(HttpRequest request, string? dxferApiKey)
{
    if (string.IsNullOrWhiteSpace(dxferApiKey))
    {
        return false;
    }

    var supplied = request.Headers["X-DXFER-API-Key"].FirstOrDefault();
    return !string.IsNullOrWhiteSpace(supplied) &&
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied.Trim()),
            Encoding.UTF8.GetBytes(dxferApiKey.Trim()));
}

static bool IsSyncLaunchRequest(HttpRequest request)
{
    if (request.Path != PathString.FromUriComponent("/"))
    {
        return false;
    }

    var launch = SyncLaunchOptionsParser.ParseQueryString(request.QueryString.Value ?? string.Empty);
    return launch.IsCallbackConfigured && launch.HasInput;
}

static string? ResolveGoogleSetting(IConfiguration configuration, string leafKey, string? environmentFallbackKey)
{
    var candidates = new[]
    {
        $"Authentication:Google:{leafKey}",
        $"GoogleAuth:{leafKey}",
        $"Google:{leafKey}"
    };

    foreach (var candidate in candidates)
    {
        var value = configuration[candidate];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
    }

    if (!string.IsNullOrWhiteSpace(environmentFallbackKey))
    {
        var envValue = Environment.GetEnvironmentVariable(environmentFallbackKey);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue.Trim();
        }
    }

    return null;
}

static string? ResolveDxferApiKey(IConfiguration configuration)
{
    var configured = configuration["Dxfer:ApiKey"];
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured.Trim();
    }

    var envValue = Environment.GetEnvironmentVariable("DXFER_API_KEY");
    return string.IsNullOrWhiteSpace(envValue) ? null : envValue.Trim();
}

static string[] ResolveAllowedGoogleDomains(IConfiguration configuration)
{
    var configured = configuration["Authentication:Google:AllowedDomains"] ??
        configuration["GoogleAuth:AllowedDomains"] ??
        configuration["Google:AllowedDomains"] ??
        Environment.GetEnvironmentVariable("GOOGLE_ALLOWED_DOMAINS");
    if (string.IsNullOrWhiteSpace(configured))
    {
        return new[] { "harrisonmetals.com" };
    }

    return configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(domain => domain.TrimStart('@').ToLowerInvariant())
        .Where(domain => !string.IsNullOrWhiteSpace(domain))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static bool IsAllowedGoogleEmail(string? email, IReadOnlyCollection<string> allowedDomains)
{
    if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
    {
        return false;
    }

    var domain = email[(email.LastIndexOf('@') + 1)..].ToLowerInvariant();
    return allowedDomains.Count == 0 || allowedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase);
}

static void LoadDotEnvFromCurrentDirectory()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory != null)
    {
        var dotEnvPath = Path.Combine(directory.FullName, ".env");
        if (File.Exists(dotEnvPath))
        {
            LoadDotEnvFile(dotEnvPath);
            return;
        }

        directory = directory.Parent;
    }
}

static void LoadDotEnvFile(string path)
{
    foreach (var rawLine in File.ReadAllLines(path))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#') || !line.Contains('=', StringComparison.Ordinal))
        {
            continue;
        }

        var separator = line.IndexOf('=', StringComparison.Ordinal);
        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim().Trim('"');
        if (key.Length == 0 || Environment.GetEnvironmentVariable(key) != null)
        {
            continue;
        }

        Environment.SetEnvironmentVariable(key, value);
    }
}
