using DXFER.Core.References.FeatureScript;

if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("""
    DXFER FeatureScript std reference indexer

    Usage:
      dotnet run --project tools/DXFER.FeatureScriptStd -- --source <stdlib-root> --output <manifest-json>
    """);
    return 0;
}

try
{
    var sourceRoot = ReadRequiredOption(args, "--source");
    var outputPath = ReadRequiredOption(args, "--output");
    var index = FeatureScriptStdIndexer.Index(sourceRoot);
    var manifestJson = FeatureScriptStdManifestWriter.Write(index, DateTimeOffset.UtcNow);

    var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
    if (!string.IsNullOrWhiteSpace(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }

    File.WriteAllText(outputPath, manifestJson);
    Console.WriteLine($"Indexed {index.ModuleCount} FeatureScript std modules.");
    Console.WriteLine($"Manifest: {Path.GetFullPath(outputPath)}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static string ReadRequiredOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    throw new ArgumentException($"Missing required option: {name}");
}
