namespace DXFER.Blazor.IO;

public static class DxfDownloadFileName
{
    public static string FromSourceName(string? sourceName)
    {
        var fileName = Path.GetFileName(sourceName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "drawing.dxf";
        }

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars().Concat(new[] { ':' }))
        {
            fileName = fileName.Replace(invalidCharacter, '-');
        }

        return Path.ChangeExtension(fileName, ".dxf");
    }

    public static string SidecarFromSourceName(string? sourceName) =>
        Path.ChangeExtension(FromSourceName(sourceName), ".dxfer.json");
}
