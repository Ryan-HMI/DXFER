using System.Globalization;
using System.Text;
using static HMISync.Web.Infrastructure.DxfCompatibility;

namespace HMISync.Web.Production;

// Export-only group edits: geometry, XDATA, source layer tables and unused blocks stay intact.
internal static class BatchDxfCutLayers
{
    internal const string CutLayer = "CUT";
    internal const string LabelLayer = "HMI_IMPORT_TEXT_NOT_CUT";
    private sealed record Record(int First, int End);
    private sealed record Edit(int Start, int End, string Text);

    internal static byte[] Consolidate(byte[] prepared, CancellationToken ct = default)
    {
        var pairs = ReadPairs(prepared, ct);
        var text = Encoding.Latin1.GetString(prepared);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string Group(int code, string value) => $"{code.ToString(CultureInfo.InvariantCulture)}{newline}{value}{newline}";
        int Offset(int index) => index < pairs.Count ? pairs[index].Start : prepared.Length;
        var edits = new List<Edit>();
        var records = new List<Record>();
        for (var i = 0; i < pairs.Count;)
        {
            var end = i + 1;
            while (end < pairs.Count && pairs[end].Code != 0) end++;
            if (pairs[i].Code != 0) { i++; continue; }
            records.Add(new(i, end));
            i = end;
        }

        int? Field(Record record, int code)
        {
            int? found = null;
            for (var i = record.First + 1; i < record.End; i++)
                if (pairs[i].Code == code)
                {
                    if (found != null) throw new InvalidDataException("Ambiguous DXF layer metadata.");
                    found = i;
                }
            return found;
        }
        string? Value(Record record, int code) => Field(record, code) is { } index ? pairs[index].Value : null;
        void Replace(int index, string value)
        {
            if (pairs[index].Value != value) edits.Add(new(Offset(index), Offset(index + 1), Group(pairs[index].Code, value)));
        }
        void Set(Record record, int code, string value, int? insertion = null)
        {
            if (Field(record, code) is { } index) Replace(index, value);
            else edits.Add(new(Offset(insertion ?? record.End), Offset(insertion ?? record.End), Group(code, value)));
        }

        var section = "";
        Record? layerTable = null;
        Record? layerTableEnd = null;
        Record? tablesEnd = null;
        Record? firstDrawingSection = null;
        var layers = new List<Record>();
        var inLayerTable = false;
        var polyline = false;
        var needsLabels = false;
        string? version = null;
        int? handseed = null;
        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            var type = pairs[record.First].Value.ToUpperInvariant();
            if (type == "SECTION")
            {
                section = record.First + 1 < record.End && pairs[record.First + 1].Code == 2
                    ? pairs[record.First + 1].Value.ToUpperInvariant() : "";
                if (section is "BLOCKS" or "ENTITIES") firstDrawingSection ??= record;
                if (section == "HEADER")
                    for (var i = record.First + 1; i + 1 < record.End; i++)
                    {
                        if (pairs[i].Code == 9 && pairs[i].Value == "$ACADVER") version = pairs[i + 1].Value;
                        if (pairs[i].Code == 9 && pairs[i].Value == "$HANDSEED" && pairs[i + 1].Code == 5) handseed = i + 1;
                    }
            }
            else if (type == "ENDSEC")
            {
                if (section == "TABLES") tablesEnd = record;
                section = "";
            }
            else if (section == "TABLES")
            {
                if (type == "TABLE")
                {
                    inLayerTable = string.Equals(Value(record, 2), "LAYER", StringComparison.OrdinalIgnoreCase);
                    if (inLayerTable)
                    {
                        if (layerTable != null) throw new InvalidDataException("Duplicate DXF layer table.");
                        layerTable = record;
                    }
                }
                else if (inLayerTable && type == "LAYER") layers.Add(record);
                else if (inLayerTable && type == "ENDTAB") { layerTableEnd = record; inLayerTable = false; }
            }
            else if (section == "ENTITIES")
            {
                var cutting = type is "LINE" or "ARC" or "CIRCLE" or "ELLIPSE" or "SPLINE" or "LWPOLYLINE" or "POLYLINE"
                    || polyline && type is "VERTEX" or "SEQEND";
                var sourceLayer = Value(record, 8) ?? "0";
                // Match the existing cleanup contract: grain marks are not cutting geometry.
                cutting &= !BatchDxfLayers.Token(sourceLayer).Contains("GRAIN", StringComparison.Ordinal);
                if (type == "POLYLINE") polyline = cutting;
                if (type == "SEQEND") polyline = false;
                var annotation = type is "TEXT" or "MTEXT";
                if (!cutting && !annotation) continue;
                if (cutting && string.Equals(sourceLayer, LabelLayer, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Cut geometry uses the reserved non-cut annotation layer. Review it before export.");
                needsLabels |= annotation;
                var insertion = record.First + 1;
                for (var i = insertion; i < record.End; i++)
                    if (pairs[i].Code == 100 && pairs[i].Value == "AcDbEntity") { insertion = i + 1; break; }
                Set(record, 8, annotation ? LabelLayer : CutLayer, insertion);
            }
        }

        var modern = version != null && string.CompareOrdinal(version, "AC1012") >= 0;
        ulong maxHandle = 0;
        foreach (var pair in pairs.Where(pair => pair.Code is 5 or 105))
            if (ulong.TryParse(pair.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var handle)) maxHandle = Math.Max(maxHandle, handle);
        var allocated = false;
        string NewHandle()
        {
            if (maxHandle >= ulong.MaxValue - 1) throw new InvalidDataException("DXF handle range is exhausted.");
            allocated = true;
            return (++maxHandle).ToString("X", CultureInfo.InvariantCulture);
        }

        var owner = layerTable == null ? modern ? NewHandle() : null : Value(layerTable, 5);
        if (modern && layerTable != null && owner == null)
            throw new InvalidDataException("Modern DXF layer table has no handle. Review its metadata before export.");
        var additions = new StringBuilder();
        var added = 0;
        foreach (var name in needsLabels ? new[] { CutLayer, LabelLayer } : new[] { CutLayer })
        {
            var matches = layers.Where(layer => string.Equals(Value(layer, 2), name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count > 1) throw new InvalidDataException("Duplicate manufacturing layer definition.");
            if (matches.SingleOrDefault() is { } existing)
            {
                Set(existing, 2, name);
                Set(existing, 70, "0");
                var color = Value(existing, 62);
                Set(existing, 62, int.TryParse(color, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                    && number is >= -255 and <= 255 && number != 0 ? Math.Abs(number).ToString(CultureInfo.InvariantCulture) : "7");
                if (modern || Field(existing, 290) != null) Set(existing, 290, name == CutLayer ? "1" : "0");
                continue;
            }
            added++;
            additions.Append(Group(0, "LAYER"));
            if (modern)
            {
                additions.Append(Group(5, NewHandle())).Append(Group(330, owner!));
                additions.Append(Group(100, "AcDbSymbolTableRecord")).Append(Group(100, "AcDbLayerTableRecord"));
            }
            additions.Append(Group(2, name)).Append(Group(70, "0")).Append(Group(62, name == CutLayer ? "7" : "3"))
                .Append(Group(6, "Continuous"));
            if (modern) additions.Append(Group(290, name == CutLayer ? "1" : "0"));
        }
        if (layerTable != null)
        {
            if (layerTableEnd == null) throw new InvalidDataException("Unclosed DXF layer table.");
            Set(layerTable, 70, (layers.Count + added).ToString(CultureInfo.InvariantCulture));
            if (added > 0) edits.Add(new(Offset(layerTableEnd.First), Offset(layerTableEnd.First), additions.ToString()));
        }
        else
        {
            var table = Group(0, "TABLE") + Group(2, "LAYER");
            if (modern) table += Group(5, owner!) + Group(330, "0") + Group(100, "AcDbSymbolTable");
            table += Group(70, added.ToString(CultureInfo.InvariantCulture)) + additions + Group(0, "ENDTAB");
            var at = tablesEnd ?? firstDrawingSection ?? throw new InvalidDataException("Missing DXF drawing section.");
            if (tablesEnd == null) table = Group(0, "SECTION") + Group(2, "TABLES") + table + Group(0, "ENDSEC");
            edits.Add(new(Offset(at.First), Offset(at.First), table));
        }
        if (allocated && handseed is { } seed) Replace(seed, (maxHandle + 1).ToString("X", CultureInfo.InvariantCulture));
        if (edits.Count == 0) return prepared;
        using var output = new MemoryStream(prepared.Length);
        var cursor = 0;
        foreach (var edit in edits.OrderBy(edit => edit.Start).ThenBy(edit => edit.End))
        {
            ct.ThrowIfCancellationRequested();
            if (edit.Start < cursor) throw new InvalidDataException("Overlapping DXF layer edits.");
            output.Write(prepared, cursor, edit.Start - cursor);
            output.Write(Encoding.Latin1.GetBytes(edit.Text));
            cursor = edit.End;
        }
        output.Write(prepared, cursor, prepared.Length - cursor);
        return output.ToArray();
    }
}
