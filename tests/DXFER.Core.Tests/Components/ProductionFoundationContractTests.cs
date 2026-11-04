using System.Xml.Linq;
using FluentAssertions;

namespace DXFER.Core.Tests.Components;

public sealed class ProductionFoundationContractTests
{
    [Fact]
    public void ProductionFoundationPreservesTheExistingAuthenticationBoundary()
    {
        var program = File.ReadAllText(FindRepositoryFile("src", "DXFER.Web", "Program.cs"));
        foreach (var addition in new[]
        {
            "Microsoft.AspNetCore.Authentication", "AddAuthentication", "AddAuthorization",
            "UseAuthentication", "UseAuthorization", "RequireAuthorization", "RequireDxferAccessAsync",
            "AddCascadingAuthenticationState", "AddGoogle", "AddCookie", "SignInSyncLaunchSessionAsync",
            "LoadDotEnvFromCurrentDirectory", "ResolveGoogleSetting", "ResolveDxferApiKey", "\"/auth/"
        })
        {
            program.Should().NotContain(addition, "this foundation must not change normal production authentication");
        }

        program.Should().Contain("app.UseAntiforgery()");
        program.Should().Contain("app.UseHttpsRedirection()");

        var project = XDocument.Load(FindRepositoryFile("src", "DXFER.Web", "DXFER.Web.csproj"));
        project.Descendants("PackageReference").Select(reference => (string?)reference.Attribute("Include"))
            .Should().NotContain("Microsoft.AspNetCore.Authentication.Google");

        var layout = File.ReadAllText(FindRepositoryFile("src", "DXFER.Web", "Components", "Layout", "MainLayout.razor"));
        layout.Should().NotContain("AuthorizeView");
        layout.Should().NotContain("/auth/");
        layout.Should().Contain("WorkbenchCommandId.RemoveDuplicates");
    }

    [Fact]
    public void ProductionNormalizeRetainsGuardedMeasurements()
    {
        var program = File.ReadAllText(FindRepositoryFile("src", "DXFER.Web", "Program.cs"));
        program.Should().Contain("ProductionDxfPreparation.Prepare(sourceText, request.HttpContext.RequestAborted)");
        program.Should().Contain("WithApiMetadata(prepared.Document, file.FileName, sourceText)");
        program.Should().Contain("catch (ProductionDxfPreparationException ex)");
        program.Should().NotContain("DxfDocumentReader.Read(sourceText)");
        program.Should().Contain("CutPathLengthService.Calculate(normalizedDocument)");
        program.Should().Contain("ClosedContourService.Calculate(normalizedDocument)");
        foreach (var field in new[] { "productionCleanup", "cutLengthInches", "cutLengthReviewReason", "units", "closedContourCount", "contourReviewReason" })
        {
            program.Should().Contain(field + " =");
        }
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{Path.Combine(segments)}'.");
    }
}
