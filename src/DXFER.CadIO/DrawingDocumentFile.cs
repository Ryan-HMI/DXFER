using DXFER.Core.Documents;
using DXFER.Core.IO;

namespace DXFER.CadIO;

/// <summary>Local file import routing. Native JSON must never fall back to a partial DXF import.</summary>
public static class DrawingDocumentFile
{
    public static bool IsNativeFileName(string fileName) =>
        fileName.EndsWith(DxferDocumentSerializer.FileExtension, StringComparison.OrdinalIgnoreCase);

    public static DrawingDocument Read(string fileName, string text) => Read(fileName, text, out _);

    public static DrawingDocument Read(string fileName, string text, out bool nativeDocumentRestored)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(text);
        var content = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (IsNativeFileName(fileName) || content.StartsWith('{') || content.StartsWith('['))
        {
            var document = DxferDocumentSerializer.Read(content);
            nativeDocumentRestored = true;
            return document;
        }
        return DxfDocumentReader.Read(text, out nativeDocumentRestored);
    }
}
