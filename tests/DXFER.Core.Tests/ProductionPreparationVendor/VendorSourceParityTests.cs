using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;

namespace DXFER.Core.Tests.ProductionPreparationVendor;

public sealed class VendorSourceParityTests
{
    private const string PinnedCommit = "f8674c7f09e974797062ba012e37f4ea2625509a";
    private const string VendorDirectory = "vendor/HmiDxfPreparation/";
    private static readonly string[] AlgorithmPaths =
    [
        "Infrastructure/DxfCompatibility.cs",
        "Infrastructure/DxfSmallCircleFilter.cs",
        "Production/BatchDxfLayers.cs",
        "Production/BatchDxfTearSlots.cs",
        "Production/BatchDxfCutLayers.cs"
    ];

    [Fact]
    public void ManifestPinsCompleteAlgorithmAndStandaloneContractInventory()
    {
        var root = FindRepositoryRoot();
        var manifest = ReadManifest(root);

        manifest.SchemaVersion.Should().Be(1);
        manifest.UpstreamRepository.Should().Be("HMI-Sync");
        manifest.UpstreamCommit.Should().Be(PinnedCommit);
        manifest.Files.Should().HaveCount(11);
        manifest.Files.Select(file => file.VendoredPath).Should().OnlyHaveUniqueItems();
        manifest.Files.Select(file => file.UpstreamPath).Should().OnlyHaveUniqueItems();
        var algorithms = manifest.Files.Where(file => file.Role == "algorithm").ToArray();
        algorithms.Select(file => file.VendoredPath).Should()
            .BeEquivalentTo(AlgorithmPaths.Select(path => VendorDirectory + path));
        foreach (var file in algorithms)
        {
            file.UpstreamPath.Should().Be("HmiSync.Web/" + file.VendoredPath[VendorDirectory.Length..]);
            file.Adaptation.Should().Be("verbatim");
            file.VendoredSha256.Should().Be(file.UpstreamSha256);
        }

        var contract = manifest.Files.Single(file => file.Role == "contract");
        contract.UpstreamPath.Should().Be("HmiSync.Web/Production/BatchExportContracts.cs");
        contract.VendoredPath.Should().Be(VendorDirectory + "Production/BatchDxfLayer.cs");
        var sources = Directory.EnumerateFiles(Path.Combine(root, VendorDirectory), "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'));
        sources.Should().BeEquivalentTo(algorithms.Append(contract).Select(file => file.VendoredPath));
        manifest.Files.Count(file => file.Role == "test").Should().Be(5);
    }

    [Fact]
    public void EveryImportedFileMatchesItsPinnedRawByteSha256()
    {
        var root = FindRepositoryRoot();
        foreach (var file in ReadManifest(root).Files)
        {
            file.UpstreamSha256.Should().MatchRegex("^[0-9a-f]{64}$");
            file.VendoredSha256.Should().MatchRegex("^[0-9a-f]{64}$");
            Path.IsPathRooted(file.VendoredPath).Should().BeFalse();
            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, file.VendoredPath))))
                .ToLowerInvariant();
            actual.Should().Be(file.VendoredSha256, "{0} must match its pinned bytes", file.VendoredPath);
            if (file.Adaptation == "verbatim")
                actual.Should().Be(file.UpstreamSha256, "{0} must remain identical to Sync", file.VendoredPath);
        }
    }

    [Fact]
    public void CadIoCompilesPinnedSourcesThroughRepositoryRelativeLinks()
    {
        var root = FindRepositoryRoot();
        var projectDirectory = Path.Combine(root, "src", "DXFER.CadIO");
        var project = XDocument.Load(Path.Combine(projectDirectory, "DXFER.CadIO.csproj"));
        var links = project.Descendants("Compile")
            .Where(element => ((string?)element.Attribute("Link"))?.StartsWith("HmiDxfPreparation/", StringComparison.Ordinal) == true)
            .ToArray();
        links.Select(element => (string?)element.Attribute("Link")).Should()
            .BeEquivalentTo(AlgorithmPaths.Append("Production/BatchDxfLayer.cs").Select(path => "HmiDxfPreparation/" + path));
        foreach (var link in links)
        {
            var include = (string)link.Attribute("Include")!;
            Path.IsPathRooted(include).Should().BeFalse();
            var relativePath = ((string)link.Attribute("Link")!)["HmiDxfPreparation/".Length..];
            include.Should().Be("../../" + VendorDirectory + relativePath);
            File.Exists(Path.Combine(projectDirectory, include)).Should().BeTrue();
        }
    }

    private static ProvenanceManifest ReadManifest(string root) =>
        JsonSerializer.Deserialize<ProvenanceManifest>(File.ReadAllText(Path.Combine(root, VendorDirectory, "provenance.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DXFER.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the DXFER checkout containing the pinned vendor sources.");
    }

    private sealed record ProvenanceManifest(int SchemaVersion, string UpstreamRepository, string UpstreamCommit, VendoredFile[] Files);
    private sealed record VendoredFile(string Role, string UpstreamPath, string UpstreamSha256,
        string VendoredPath, string VendoredSha256, string Adaptation);
}
