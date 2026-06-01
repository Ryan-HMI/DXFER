namespace DXFER.Blazor.IO;

public static class DxfDownloadFileName
{
    public static string FromSourceName(string? sourceName)
    {
        var normalizedSource = sourceName?.Replace('\\', Path.DirectorySeparatorChar);
        var fileName = Path.GetFileName(normalizedSource);
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
