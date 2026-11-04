using System.Globalization;
using System.Text;

namespace HMISync.Web.Infrastructure;

// Older DXFer saves used application IDs as handles and omitted post-R12 type metadata.
// Repair that format and unreferenced AC1015 files missing space records; never resample curves.
internal static class DxfCompatibility
{
    internal const int MaximumBytes = 50 * 1024 * 1024;
    internal sealed record Pair(int Code, string Value, int Start);

    internal static byte[] PrepareDownload(byte[] bytes, CancellationToken ct = default)
    {
        // Native binary DXFs are not produced by the legacy text writer.
        if (bytes.AsSpan().StartsWith("AutoCAD Binary DXF"u8)) return bytes;
        return Repair(ReadPairs(bytes, ct)) ?? bytes;
    }

    internal static List<Pair> ReadPairs(byte[] bytes, CancellationToken ct)
    {
        if (bytes.Length > MaximumBytes || bytes.Contains((byte)0))
            throw new InvalidDataException("Binary or oversized DXF requires manual preparation.");
        var text = Encoding.Latin1.GetString(bytes);
        var pairs = new List<Pair>();
        var offset = text.StartsWith("\u00ef\u00bb\u00bf", StringComparison.Ordinal) ? 3 : 0;
        using var reader = new StringReader(text[offset..]);
        while (reader.ReadLine() is { } rawCode)
        {
            ct.ThrowIfCancellationRequested();
            var start = offset;
            if (string.IsNullOrWhiteSpace(rawCode) && string.IsNullOrWhiteSpace(text[start..])) break;
            NextLine();
            var value = reader.ReadLine();
            if (value == null || !int.TryParse(rawCode.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) || code is < 0 or > 1071)
                throw new InvalidDataException("Malformed DXF group records require manual preparation.");
            NextLine();
            pairs.Add(new(code, value.Trim(), start));
            if (pairs.Count > 500_000) throw new InvalidDataException("DXF scan exceeds the record limit.");
        }
        void NextLine() { var newline = text.IndexOf('\n', offset); offset = newline < 0 ? text.Length : newline + 1; }
        if (text.Contains('\r') && !text.Contains("\r\n", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported DXF line endings require manual preparation.");
        return pairs;
    }

    private static readonly Dictionary<string, string> Subclasses = new(StringComparer.Ordinal)
    {
        ["LINE"] = "AcDbLine",
        ["ARC"] = "AcDbCircle",
        ["CIRCLE"] = "AcDbCircle",
        ["SPLINE"] = "AcDbSpline",
        ["ELLIPSE"] = "AcDbEllipse",
        ["POINT"] = "AcDbPoint",
        ["LWPOLYLINE"] = "AcDbPolyline",
        ["TEXT"] = "AcDbText"
    };

    internal static byte[]? Repair(IReadOnlyList<Pair> pairs)
    {
        static bool LegacyId(string value)
        {
            var dash = value.IndexOf('-');
            return dash > 0 && value[..dash] is "line" or "arc" or "circle" or "ellipse" or "spline" or "point" or "polyline" or "polygon";
        }
        var legacy = pairs.Any(p => p.Code == 5 && LegacyId(p.Value));
        var hasVersion = pairs.Any(p => p.Code == 9 && p.Value == "$ACADVER");
        var missingSpaces = hasVersion && pairs.Any(p => p.Code == 1 && p.Value == "AC1015") &&
            !pairs.Any(p => p.Code == 2 && p.Value == "BLOCKS" || p.Code == 0 && p.Value == "BLOCK_RECORD");
        if (!legacy && !missingSpaces) return null;
        if (pairs.Any(p => p.Code is 102 or >= 320 and <= 369 or >= 390 and <= 399 or >= 1000 ||
                           legacy && (p.Code == 100 || p.Code == 9 && p.Value == "$ACADVER")))
            throw new InvalidDataException("Legacy DXFer handles mixed with modern metadata or references require manual DXF review.");
        if (pairs.Any(p => p.Code == 100 && !Subclasses.ContainsValue(p.Value) &&
            p.Value is not ("AcDbEntity" or "AcDbArc" or "AcDbSymbolTable" or "AcDbSymbolTableRecord" or "AcDbLayerTableRecord" or "AcDbLinetypeTableRecord")))
            throw new InvalidDataException("Unsupported DXF subclass requires manual review.");

        var output = new StringBuilder();
        void Pair(int code, string value) => output.Append(code.ToString(CultureInfo.InvariantCulture)).Append('\n').Append(value).Append('\n');
        var hasHeader = pairs.Any(p => p.Code == 2 && p.Value == "HEADER");
        var hasTables = pairs.Any(p => p.Code == 2 && p.Value == "TABLES");
        void SpaceTable()
        {
            Pair(0, "TABLE"); Pair(2, "BLOCK_RECORD"); Pair(5, "1"); Pair(330, "0");
            Pair(100, "AcDbSymbolTable"); Pair(70, "2");
            foreach (var (name, id) in new[] { ("*Model_Space", "2"), ("*Paper_Space", "3") })
            {
                Pair(0, "BLOCK_RECORD"); Pair(5, id); Pair(330, "1");
                Pair(100, "AcDbSymbolTableRecord"); Pair(100, "AcDbBlockTableRecord"); Pair(2, name);
            }
            Pair(0, "ENDTAB");
        }
        void SpaceBlocks()
        {
            Pair(0, "SECTION"); Pair(2, "BLOCKS");
            foreach (var (name, owner, begin, end) in new[] { ("*Model_Space", "2", "4", "5"), ("*Paper_Space", "3", "6", "7") })
            {
                Pair(0, "BLOCK"); Pair(5, begin); Pair(330, owner);
                Pair(100, "AcDbEntity"); Pair(8, "0"); Pair(100, "AcDbBlockBegin");
                Pair(2, name); Pair(70, "0"); Pair(10, "0"); Pair(20, "0"); Pair(30, "0"); Pair(3, name); Pair(1, "");
                Pair(0, "ENDBLK"); Pair(5, end); Pair(330, owner);
                Pair(100, "AcDbEntity"); Pair(8, "0"); Pair(100, "AcDbBlockEnd");
            }
            Pair(0, "ENDSEC");
        }
        if (!hasHeader)
        {
            Pair(0, "SECTION"); Pair(2, "HEADER"); Pair(9, "$ACADVER"); Pair(1, "AC1015"); Pair(0, "ENDSEC");
        }
        var section = "";
        var handle = 256;
        for (var i = 0; i < pairs.Count;)
        {
            var record = pairs[i];
            var end = i + 1;
            while (end < pairs.Count && pairs[end].Code != 0) end++;
            var fields = pairs.Skip(i + 1).Take(end - i - 1).ToArray();
            var type = record.Value;
            if (record.Code != 0) throw new InvalidDataException("Unsupported legacy DXF record.");
            if (type == "SECTION")
            {
                section = fields.FirstOrDefault(p => p.Code == 2)?.Value ?? "";
                if (section is not ("HEADER" or "TABLES" or "ENTITIES"))
                    throw new InvalidDataException("Legacy DXF contains unsupported sections; review in DXFer.");
                if (section == "ENTITIES")
                {
                    if (!hasTables) { Pair(0, "SECTION"); Pair(2, "TABLES"); SpaceTable(); Pair(0, "ENDSEC"); }
                    SpaceBlocks();
                }
                Pair(0, type);
                foreach (var p in fields)
                {
                    Pair(p.Code, p.Value);
                    if (section == "HEADER" && p.Code == 2 && !hasVersion) { Pair(9, "$ACADVER"); Pair(1, "AC1015"); }
                }
            }
            else if (type is "ENDSEC" or "EOF" or "ENDTAB")
            {
                if (type == "ENDSEC" && section == "TABLES") SpaceTable();
                Pair(0, type);
                foreach (var p in fields) Pair(p.Code, p.Value);
                if (type == "ENDSEC") section = "";
            }
            else if (section == "ENTITIES" && Subclasses.TryGetValue(type, out var subclass))
            {
                if (fields.Any(p => p.Code is not (5 or 8 or 6 or 62 or 10 or 20 or 30 or 11 or 21 or 31 or 40 or 41 or 42 or 50 or 51 or 70 or 71 or 72 or 73 or 74 or 90 or 1 or 100)))
                    throw new InvalidDataException("Legacy DXF contains unsupported entity properties; review in DXFer.");
                Pair(0, type); Pair(5, (handle++).ToString("X", CultureInfo.InvariantCulture)); Pair(330, "2"); Pair(100, "AcDbEntity");
                foreach (var p in fields.Where(p => p.Code is 8 or 6 or 62)) Pair(p.Code, p.Value);
                Pair(100, subclass);
                var geometry = fields.Where(p => p.Code is not (5 or 8 or 6 or 62 or 100)).ToArray();
                for (var g = 0; g < geometry.Length; g++)
                {
                    var p = geometry[g];
                    if (type == "ARC" && p.Code == 50) Pair(100, "AcDbArc");
                    Pair(p.Code, p.Value);
                    if (type != "LWPOLYLINE" && p.Code is 20 or 21 && (g + 1 == geometry.Length || geometry[g + 1].Code != p.Code + 10))
                        Pair(p.Code + 10, "0");
                }
                if (type == "TEXT") Pair(100, "AcDbText");
            }
            else if (section == "TABLES" && type is "LAYER" or "LTYPE" or "TABLE")
            {
                Pair(0, type);
                if (type != "TABLE")
                {
                    Pair(5, (handle++).ToString("X", CultureInfo.InvariantCulture));
                    Pair(100, "AcDbSymbolTableRecord");
                    Pair(100, type == "LAYER" ? "AcDbLayerTableRecord" : "AcDbLinetypeTableRecord");
                }
                foreach (var p in fields)
                {
                    if (p.Code == 5 && legacy) throw new InvalidDataException("Unexpected legacy DXF table handles.");
                    if (p.Code is 5 or 100) continue;
                    Pair(p.Code, p.Value);
                    if (type == "TABLE" && p.Code == 2)
                    {
                        Pair(5, (handle++).ToString("X", CultureInfo.InvariantCulture));
                        Pair(100, "AcDbSymbolTable");
                    }
                }
            }
            else throw new InvalidDataException("Legacy DXF contains unsupported records; review in DXFer.");
            i = end;
        }
        return Encoding.Latin1.GetBytes(output.ToString());
    }
}
