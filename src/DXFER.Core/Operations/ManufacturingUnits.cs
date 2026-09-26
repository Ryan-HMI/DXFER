using DXFER.Core.Documents;

namespace DXFER.Core.Operations;

public static class ManufacturingUnits
{
    // HMI manufacturing coordinates are inches, regardless of legacy DXF headers.
    // Stamp exports consistently without scaling geometry or dimensions.
    public static DrawingDocument AssumeInches(DrawingDocument document) => new(
        document.Entities, document.Dimensions, document.Constraints,
        document.Metadata with
        {
            Units = DrawingUnits.Inches,
            Warnings = document.Metadata.Warnings.Where(w => w.Code != "missing-units").ToArray()
        });
}
