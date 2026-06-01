namespace DXFER.Blazor.IO;

public static class DxfDownloadFileName
{
    public static string FromSourceName(string? sourceName)
    {
        var normalizedSourceName = sourceName?.Replace('\\', '/');
        var fileName = Path.GetFileName(normalizedSourceName);
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
}
