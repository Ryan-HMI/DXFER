using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DXFER.Core.References.FeatureScript;

public static class FeatureScriptStdIndexer
{
    private static readonly Regex ImportRegex = new(
        @"(?<export>\bexport\s+)?\bimport\s*\(\s*path\s*:\s*""(?<path>[^""]+)""\s*,\s*version\s*:\s*""(?<version>[^""]*)""\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex ExportRegex = new(
        @"\bexport\s+(?<kind>function|type|predicate|enum|const)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BuiltinCallRegex = new(
        @"@(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static FeatureScriptStdIndex Index(string sourceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);

        var fullRoot = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"FeatureScript std source root was not found: {fullRoot}");
        }

        var modules = Directory
            .EnumerateFiles(fullRoot, "*.fs", SearchOption.AllDirectories)
            .Select(path => new
            {
                FullPath = path,
                RelativePath = NormalizePath(Path.GetRelativePath(fullRoot, path))
            })
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .Select(file => IndexFile(file.FullPath, file.RelativePath))
            .ToArray();

        return new FeatureScriptStdIndex(
            fullRoot,
            FindLicenseRelativePath(fullRoot),
            modules);
    }

    private static FeatureScriptStdModule IndexFile(string fullPath, string relativePath)
    {
        var sourceText = File.ReadAllText(fullPath);
        var sourceWithoutComments = StripComments(sourceText);
        var modulePath = $"onshape/std/{relativePath}";

        return new FeatureScriptStdModule(
            modulePath,
            relativePath,
            ComputeSha256(sourceText),
            Encoding.UTF8.GetByteCount(sourceText),
            CountLines(sourceText),
            ParseImports(sourceWithoutComments),
            ParseExports(sourceWithoutComments),
            ParseBuiltinCalls(sourceWithoutComments));
    }

    private static string? FindLicenseRelativePath(string fullRoot)
    {
        foreach (var fileName in new[] { "LICENSE.txt", "LICENSE" })
        {
            var fullPath = Path.Combine(fullRoot, fileName);
            if (File.Exists(fullPath))
            {
                return fileName;
            }
        }

        return null;
    }

    private static IReadOnlyList<FeatureScriptStdImport> ParseImports(string sourceText)
    {
        var imports = new List<FeatureScriptStdImport>();
        var seen = new HashSet<FeatureScriptStdImport>();

        foreach (Match match in ImportRegex.Matches(sourceText))
        {
            var import = new FeatureScriptStdImport(
                match.Groups["path"].Value,
                match.Groups["export"].Success);
            if (seen.Add(import))
            {
                imports.Add(import);
            }
        }

        return imports;
    }

    private static IReadOnlyList<FeatureScriptStdExport> ParseExports(string sourceText)
    {
        var exports = new List<FeatureScriptStdExport>();
        var seen = new HashSet<FeatureScriptStdExport>();

        foreach (Match match in ExportRegex.Matches(sourceText))
        {
            var export = new FeatureScriptStdExport(
                match.Groups["kind"].Value,
                match.Groups["name"].Value);
            if (seen.Add(export))
            {
                exports.Add(export);
            }
        }

        return exports;
    }

    private static IReadOnlyList<string> ParseBuiltinCalls(string sourceText)
    {
        var builtinCalls = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in BuiltinCallRegex.Matches(sourceText))
        {
            var builtinCall = match.Groups["name"].Value;
            if (seen.Add(builtinCall))
            {
                builtinCalls.Add(builtinCall);
            }
        }

        return builtinCalls;
    }

    private static string ComputeSha256(string sourceText)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sourceText));
        return string.Concat(bytes.Select(part => part.ToString("x2")));
    }

    private static int CountLines(string sourceText) =>
        sourceText.Length == 0
            ? 0
            : sourceText.Count(character => character == '\n') + 1;

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/');

    private static string StripComments(string sourceText)
    {
        var result = new StringBuilder(sourceText.Length);
        var inString = false;
        var escaped = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = 0; i < sourceText.Length; i++)
        {
            var current = sourceText[i];
            var next = i + 1 < sourceText.Length ? sourceText[i + 1] : '\0';

            if (inLineComment)
            {
                if (current is '\r' or '\n')
                {
                    inLineComment = false;
                    result.Append(current);
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                    continue;
                }

                if (current is '\r' or '\n')
                {
                    result.Append(current);
                }

                continue;
            }

            if (inString)
            {
                result.Append(current);

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                result.Append(current);
                continue;
            }

            if (current == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            result.Append(current);
        }

        return result.ToString();
    }
}
