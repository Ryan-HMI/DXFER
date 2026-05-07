using FluentAssertions;

namespace DXFER.Core.Tests.IO;

public sealed class CadIoBoundaryTests
{
    [Fact]
    public void SolutionContainsCadIoProject()
    {
        var solution = File.ReadAllText(FindRepositoryFile("DXFER.slnx"));

        solution.Should().Contain("src/DXFER.CadIO/DXFER.CadIO.csproj");
    }

    [Fact]
    public void CoreProjectDoesNotOwnDxfReaderOrWriter()
    {
        FindRepositoryFileOrNull("src", "DXFER.Core", "IO", "DxfDocumentReader.cs")
            .Should().BeNull();
        FindRepositoryFileOrNull("src", "DXFER.Core", "IO", "DxfDocumentWriter.cs")
            .Should().BeNull();
    }

    [Fact]
    public void CadIoProjectReferencesCoreAndBlazorReferencesCadIo()
    {
        var cadIoProject = File.ReadAllText(FindRepositoryFile("src", "DXFER.CadIO", "DXFER.CadIO.csproj"));
        var blazorProject = File.ReadAllText(FindRepositoryFile("src", "DXFER.Blazor", "DXFER.Blazor.csproj"));
        var webProject = File.ReadAllText(FindRepositoryFile("src", "DXFER.Web", "DXFER.Web.csproj"));
        var testProject = File.ReadAllText(FindRepositoryFile("tests", "DXFER.Core.Tests", "DXFER.Core.Tests.csproj"));

        cadIoProject.Should().Contain("..\\DXFER.Core\\DXFER.Core.csproj");
        blazorProject.Should().Contain("..\\DXFER.CadIO\\DXFER.CadIO.csproj");
        webProject.Should().Contain("..\\DXFER.CadIO\\DXFER.CadIO.csproj");
        testProject.Should().Contain("..\\..\\src\\DXFER.CadIO\\DXFER.CadIO.csproj");
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var found = FindRepositoryFileOrNull(segments);
        if (found is not null)
        {
            return found;
        }

        throw new FileNotFoundException($"Could not locate repository file '{Path.Combine(segments)}'.");
    }

    private static string? FindRepositoryFileOrNull(params string[] segments)
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

        return null;
    }
}
