namespace DXFER.Core.References.FeatureScript;

public sealed record FeatureScriptStdIndex(
    string SourceRoot,
    string? LicenseRelativePath,
    IReadOnlyList<FeatureScriptStdModule> Modules)
{
    public int ModuleCount => Modules.Count;
}

public sealed record FeatureScriptStdModule(
    string ModulePath,
    string RelativePath,
    string Sha256,
    int ByteCount,
    int LineCount,
    IReadOnlyList<FeatureScriptStdImport> Imports,
    IReadOnlyList<FeatureScriptStdExport> Exports,
    IReadOnlyList<string> BuiltinCalls);

public sealed record FeatureScriptStdImport(string ModulePath, bool IsReExport);

public sealed record FeatureScriptStdExport(string Kind, string Name);
