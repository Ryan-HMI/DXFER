namespace DXFER.Core.Documents;

public sealed record DxfEntityStyle(
    string? LayerName = null,
    string? LineTypeName = null,
    int? ColorNumber = null);
