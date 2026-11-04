using System.Globalization;
using System.Security.Cryptography;

namespace HMISync.Web.Infrastructure;

internal static class DxfSmallCircleFilter
{
    internal const decimal MinimumDiameterInches = 0.050m;
    internal sealed record Result(byte[] Bytes, int RemovedCount, int ProtectedCount);

    internal static async Task<string> SaveWithOriginalAsync(string path, byte[] original, byte[] cleaned, CancellationToken ct)
    {
        var backup = path + ".original-" + Convert.ToHexString(SHA256.HashData(original)) + ".dxf";
        if (!File.Exists(backup))
        {
            await using var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await stream.WriteAsync(original, ct);
        }
        var saved = await File.ReadAllBytesAsync(backup, ct);
        if (!original.AsSpan().SequenceEqual(saved))
            throw new IOException("Original DXF backup could not be verified; upload cleanup was not saved.");
        var temporary = path + ".cleanup-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporary, cleaned, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return Path.GetFileName(backup);
    }

    // Sync flat-pattern coordinates are inches. Copy untouched byte ranges rather
    // than reserializing curves or applying a general small-entity cleanup.
    internal static Result Apply(byte[] source, CancellationToken ct = default)
    {
        var pairs = DxfCompatibility.ReadPairs(source, ct);
        var removals = new List<(int Start, int End)>();
        var section = "";
        var protectedCount = 0;
        var native = pairs.Any(p => p.Value == "DXFER_NATIVE_V1");
        var referenced = pairs.Where(p => p.Code is >= 320 and <= 369 or >= 390 and <= 399 or 1005)
            .Select(p => p.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < pairs.Count;)
        {
            ct.ThrowIfCancellationRequested();
            var record = pairs[i];
            var end = i + 1;
            while (end < pairs.Count && pairs[end].Code != 0) end++;
            var fields = pairs.GetRange(i + 1, end - i - 1);
            if (record.Code == 0 && record.Value == "SECTION") section = fields.FirstOrDefault(p => p.Code == 2)?.Value ?? "";
            if (record.Code == 0 && record.Value == "ENDSEC") section = "";
            if (section == "ENTITIES" && record.Code == 0 && record.Value == "CIRCLE"
                && !fields.Any(p => p.Code == 67 && p.Value != "0" || p.Code == 410 && !p.Value.Equals("Model", StringComparison.OrdinalIgnoreCase)))
            {
                var radii = fields.Where(p => p.Code == 40).ToArray();
                if (radii.Length == 1 && decimal.TryParse(radii[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius)
                    && radius > 0 && radius < MinimumDiameterInches / 2)
                {
                    var handle = fields.FirstOrDefault(p => p.Code == 5)?.Value;
                    if (native || handle is not null && referenced.Contains(handle)) protectedCount++;
                    else removals.Add((record.Start, end < pairs.Count ? pairs[end].Start : source.Length));
                }
            }
            i = end;
        }
        if (removals.Count == 0) return new(source, 0, protectedCount);
        if (section != "" || !pairs.Any(p => p.Code == 0 && p.Value == "EOF"))
            throw new InvalidDataException("Incomplete DXF; small-circle cleanup was not applied.");
        using var output = new MemoryStream(source.Length);
        var offset = 0;
        foreach (var removal in removals)
        {
            output.Write(source.AsSpan(offset, removal.Start - offset));
            offset = removal.End;
        }
        output.Write(source.AsSpan(offset));
        return new(output.ToArray(), removals.Count, protectedCount);
    }
}
