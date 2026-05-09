using DXFER.Core.References.FeatureScript;
using FluentAssertions;

namespace DXFER.Core.Tests.References;

public sealed class FeatureScriptStdIndexerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dxfer-featurescript-std-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void IndexesImportsExportsAndBuiltinCallsFromFeatureScriptFiles()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "LICENSE.txt"), "MIT license text");
        File.WriteAllText(
            Path.Combine(_root, "sketch.fs"),
            """
            FeatureScript 1;
            // import(path : "onshape/std/commented.fs", version : "");
            /* export import(path : "onshape/std/commentedExport.fs", version : ""); */
            export import(path : "onshape/std/query.fs", version : "");
            import(
                path : "onshape/std/mathUtils.fs",
                version : ""
            );

            export type Sketch typecheck canBeSketch;
            export predicate canBeSketch(value) { @isSketch(value); }
            export enum DimensionDirection { MINIMUM, HORIZONTAL, VERTICAL }
            export const sketchBounds = {};
            export function skConstraint(sketch is Sketch, constraintId is string, value is map)
            {
                return @skConstraint(sketch, constraintId, value);
            }
            """);

        var index = FeatureScriptStdIndexer.Index(_root);

        index.SourceRoot.Should().Be(_root);
        index.LicenseRelativePath.Should().Be("LICENSE.txt");
        index.Modules.Should().ContainSingle();
        var module = index.Modules[0];
        module.RelativePath.Should().Be("sketch.fs");
        module.ModulePath.Should().Be("onshape/std/sketch.fs");
        module.LineCount.Should().BeGreaterThan(10);
        module.Sha256.Should().HaveLength(64);
        module.Imports.Should().BeEquivalentTo(
            new[]
            {
                new FeatureScriptStdImport("onshape/std/query.fs", true),
                new FeatureScriptStdImport("onshape/std/mathUtils.fs", false)
            },
            options => options.WithStrictOrdering());
        module.Exports.Should().BeEquivalentTo(
            new[]
            {
                new FeatureScriptStdExport("type", "Sketch"),
                new FeatureScriptStdExport("predicate", "canBeSketch"),
                new FeatureScriptStdExport("enum", "DimensionDirection"),
                new FeatureScriptStdExport("const", "sketchBounds"),
                new FeatureScriptStdExport("function", "skConstraint")
            },
            options => options.WithStrictOrdering());
        module.BuiltinCalls.Should().Equal("isSketch", "skConstraint");
    }

    [Fact]
    public void IndexesFeatureScriptFilesRecursivelyInStableOrder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        File.WriteAllText(Path.Combine(_root, "zLast.fs"), "export function zLast() {}");
        File.WriteAllText(Path.Combine(_root, "nested", "aFirst.fs"), "export function aFirst() {}");

        var index = FeatureScriptStdIndexer.Index(_root);

        index.ModuleCount.Should().Be(2);
        index.Modules.Select(module => module.RelativePath).Should().Equal(
            "nested/aFirst.fs",
            "zLast.fs");
    }

    [Fact]
    public void WritesDeterministicManifestJsonForIndexedModules()
    {
        var index = new FeatureScriptStdIndex(
            "C:/std",
            "LICENSE.txt",
            new[]
            {
                new FeatureScriptStdModule(
                    "onshape/std/sketch.fs",
                    "sketch.fs",
                    "abc123",
                    ByteCount: 42,
                    LineCount: 7,
                    new[]
                    {
                        new FeatureScriptStdImport("onshape/std/query.fs", true)
                    },
                    new[]
                    {
                        new FeatureScriptStdExport("function", "skSolve")
                    },
                    new[] { "skSolve" })
            });
        var generatedAt = new DateTimeOffset(2026, 5, 9, 12, 30, 0, TimeSpan.Zero);

        var json = FeatureScriptStdManifestWriter.Write(index, generatedAt);

        json.ReplaceLineEndings("\n").Should().Be("""
        {
          "generatedAtUtc": "2026-05-09T12:30:00+00:00",
          "sourceRoot": "C:/std",
          "licenseRelativePath": "LICENSE.txt",
          "moduleCount": 1,
          "modules": [
            {
              "modulePath": "onshape/std/sketch.fs",
              "relativePath": "sketch.fs",
              "sha256": "abc123",
              "byteCount": 42,
              "lineCount": 7,
              "imports": [
                {
                  "modulePath": "onshape/std/query.fs",
                  "isReExport": true
                }
              ],
              "exports": [
                {
                  "kind": "function",
                  "name": "skSolve"
                }
              ],
              "builtinCalls": [
                "skSolve"
              ]
            }
          ]
        }
        """.ReplaceLineEndings("\n"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
