using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.IO;

public sealed class DxferSidecarWriterTests
{
    [Fact]
    public void WritesDeterministicSidecarJsonFromDocumentMetadata()
    {
        var metadata = DrawingDocumentMetadata.Empty with
        {
            SourceFileName = "source.dxf",
            NormalizedFileName = "source.normalized.dxf",
            Units = DrawingUnits.Millimeters,
            Mode = DrawingDocumentMode.ReferenceOnly,
            TrustedSource = true,
            Warnings = new[]
            {
                new DrawingDocumentWarning(
                    "unsupported-entity",
                    DrawingDocumentWarningSeverity.Warning,
                    "Skipped unsupported entity.")
            },
            UnsupportedEntityCounts = new Dictionary<string, int>
            {
                ["3DSOLID"] = 1
            }
        };
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(3, 4)),
                new PointEntity(EntityId.Create("construction"), new Point2(1, 1), IsConstruction: true)
            },
            new[]
            {
                new SketchDimension(
                    "dim-1",
                    SketchDimensionKind.LinearDistance,
                    new[] { "edge:start", "edge:end" },
                    5)
            },
            new[]
            {
                new SketchConstraint(
                    "horizontal-1",
                    SketchConstraintKind.Horizontal,
                    new[] { "edge" },
                    SketchConstraintState.Unsatisfied)
            },
            metadata);

        var json = DxferSidecarWriter.Write(
            document,
            sourceContent: "abc",
            normalizedContent: "normalized dxf");

        json.ReplaceLineEndings("\n").Should().Be("""
{
  "schemaVersion": 1,
  "source": {
    "fileName": "source.dxf",
    "sha256": "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
  },
  "normalized": {
    "fileName": "source.normalized.dxf",
    "sha256": "a5a34cbd80c6d21a013fb153a265523a94b40f311bb3edcbb5cc1956a481f497"
  },
  "units": "millimeters",
  "mode": "referenceOnly",
  "trustedSource": true,
  "bounds": {
    "minX": 0,
    "minY": 0,
    "maxX": 3,
    "maxY": 4,
    "width": 3,
    "height": 4,
    "diagonal": 5
  },
  "normalization": {
    "entityCount": 2,
    "exportedEntityCount": 1,
    "constructionEntityCount": 1,
    "dimensionCount": 1,
    "constraintCount": 1
  },
  "grain": {},
  "warnings": [
    {
      "code": "unsupported-entity",
      "severity": "warning",
      "message": "Skipped unsupported entity."
    }
  ],
  "unsupportedEntityCounts": {
    "3DSOLID": 1
  }
}
""".ReplaceLineEndings("\n"));
    }

    [Fact]
    public void UsesMetadataHashesWhenContentIsNotProvided()
    {
        var metadata = DrawingDocumentMetadata.Empty with
        {
            SourceSha256 = "source-hash",
            NormalizedSha256 = "normalized-hash"
        };
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(1, 0))
            },
            Array.Empty<SketchDimension>(),
            Array.Empty<SketchConstraint>(),
            metadata);

        var sidecar = DxferSidecarWriter.Create(document);

        sidecar.Source.Sha256.Should().Be("source-hash");
        sidecar.Normalized.Sha256.Should().Be("normalized-hash");
    }
}
