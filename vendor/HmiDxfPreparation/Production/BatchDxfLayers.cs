using System.Text;
using HMISync.Web.Infrastructure;
using static HMISync.Web.Infrastructure.DxfCompatibility;

namespace HMISync.Web.Production;

internal sealed record BatchDxfLayerResult(byte[] Bytes, IReadOnlyList<BatchDxfLayer> Layers, int TearSlotsCreated = 0, int ExistingTearSlots = 0);

// Lossless DXF group records: retain source spans instead of regenerating geometry.
internal static class BatchDxfLayers
{
    internal sealed record Entity(string Type, string Layer, int Start, int End, IReadOnlyList<Pair> Fields);
    internal static string Token(string name) => new(name.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    internal static bool Bend(string name) => Token(name) is "BEND" or "BENDS" or "BENDLINE" or "BENDLINES" or
        "BENDUP" or "BENDDOWN" or "BENDLINESUP" or "BENDLINESDOWN" or "SHEETMETALBENDLINESUP" or "SHEETMETALBENDLINESDOWN";
    internal static bool Cut(string name) => Token(name) is "0" or "CUT" or "CUTLINES" or "SHEETMETALCUTLINES" or "OUTLINE" or "OUTERPROFILE" or "INNERPROFILE";

    internal static BatchDxfLayerResult Prepare(byte[] bytes, IReadOnlyCollection<string> excluded, CancellationToken ct = default)
    {
        // Latin1 maps each byte to one character, preserving legacy encodings and exact offsets.
        var text = Encoding.Latin1.GetString(bytes);
        var pairs = ReadPairs(bytes, ct);

        var entities = new List<Entity>();
        var section = "";
        var seenEntities = false;
        var eof = false;
        Entity? polyline = null;
        for (var i = 0; i < pairs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var pair = pairs[i];
            if (eof) throw new InvalidDataException("DXF contains records after EOF.");
            if (pair.Code != 0) continue;
            var type = pair.Value.ToUpperInvariant();
            if (type == "SECTION")
            {
                if (section.Length > 0 || i + 1 >= pairs.Count || pairs[i + 1].Code != 2)
                    throw new InvalidDataException("Malformed DXF section.");
                section = pairs[i + 1].Value.ToUpperInvariant();
                if (section == "ENTITIES")
                {
                    if (seenEntities) throw new InvalidDataException("Duplicate DXF entity section.");
                    seenEntities = true;
                }
                continue;
            }
            if (type == "ENDSEC")
            {
                if (section.Length == 0 || polyline != null) throw new InvalidDataException("Incomplete DXF entity sequence.");
                section = "";
                continue;
            }
            if (type == "EOF")
            {
                if (section.Length > 0) throw new InvalidDataException("Unclosed DXF section.");
                eof = true;
                continue;
            }
            if (section != "ENTITIES") continue;
            var end = i + 1;
            while (end < pairs.Count && pairs[end].Code != 0) end++;
            var fields = pairs.GetRange(i + 1, end - i - 1);
            var layers = fields.Where(p => p.Code == 8).Select(p => p.Value).ToList();
            if (layers.Count > 1 || layers.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Ambiguous DXF entity layer.");
            var layer = layers.SingleOrDefault() ?? "0";
            var entity = new Entity(type, layer, pair.Start, end < pairs.Count ? pairs[end].Start : text.Length, fields);
            if (polyline != null)
            {
                if (type is not ("VERTEX" or "SEQEND") || (layer != "0" && !string.Equals(layer, polyline.Layer, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Mixed-layer or malformed polyline requires manual preparation.");
                if (type == "SEQEND") { entities.Add(polyline with { End = entity.End }); polyline = null; }
            }
            else if (type == "POLYLINE") polyline = entity;
            else if (type is "INSERT" or "DIMENSION" or "ACAD_PROXY_ENTITY")
                throw new InvalidDataException("Block references or proxy geometry require manual layer preparation before export.");
            else if (type is "VERTEX" or "SEQEND" or "ATTRIB") throw new InvalidDataException("Orphan DXF sequence record.");
            else entities.Add(entity);
            i = end - 1;
        }
        if (!eof || !seenEntities || polyline != null || entities.Count == 0)
            throw new InvalidDataException("DXF has no complete entity section.");

        if (DxfCompatibility.Repair(pairs) is { } compatible)
            return Prepare(compatible, excluded, ct);

        var slots = BatchDxfTearSlots.Prepare(entities, pairs, ct);
        var selected = excluded.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inventory = entities.GroupBy(e => e.Layer, StringComparer.OrdinalIgnoreCase)
            .Select(g => new BatchDxfLayer(g.Key, g.Count(), Bend(g.Key) || slots.CleanupLayer(g.Key),
                !Bend(g.Key) && !Cut(g.Key) && !slots.CleanupLayer(g.Key),
                Bend(g.Key) || selected.Contains(g.Key) || g.All(e => slots.Replacements.ContainsKey(e.Start))))
            .OrderBy(l => l.Name, StringComparer.Ordinal).ToList();
        if (selected.Any(name => !inventory.Any(l => l.RequiresReview && string.Equals(name, l.Name, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("Selected layer is absent or is a protected cut layer. Refresh layer review.");
        var removedNames = inventory.Where(l => l.Removed).Select(l => l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = entities.Where(e => removedNames.Contains(e.Layer) || slots.Replacements.ContainsKey(e.Start)).ToList();
        if (!entities.Any(e => !removedNames.Contains(e.Layer) && !Token(e.Layer).Contains("GRAIN", StringComparison.Ordinal) &&
            e.Type is "LINE" or "ARC" or "CIRCLE" or "ELLIPSE" or "SPLINE" or "LWPOLYLINE" or "POLYLINE"))
            throw new InvalidDataException("Layer removal leaves no supported cutting geometry.");
        if (removed.Count == 0) return new(bytes, inventory, slots.Created, slots.Existing);

        var spans = removed.OrderBy(e => e.Start).ToList();
        var spanIndex = 0;
        bool IsRemoved(int start)
        {
            while (spanIndex < spans.Count && start >= spans[spanIndex].End) spanIndex++;
            return spanIndex < spans.Count && start >= spans[spanIndex].Start;
        }
        var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in pairs)
            if (IsRemoved(p.Start) && p.Code == 5) handles.Add(p.Value);
        spanIndex = 0;
        foreach (var p in pairs)
            if (!IsRemoved(p.Start) && ((p.Code is >= 320 and <= 369 or >= 390 and <= 399 or 1005 or 5) && handles.Contains(p.Value)))
                throw new InvalidDataException("Removed geometry is referenced by retained DXF records. Review it in DXFer.");
        using var output = new MemoryStream(bytes.Length);
        var cursor = 0;
        foreach (var span in spans)
        {
            output.Write(bytes, cursor, span.Start - cursor);
            if (slots.Replacements.TryGetValue(span.Start, out var replacement))
                output.Write(Encoding.ASCII.GetBytes(replacement));
            cursor = span.End;
        }
        output.Write(bytes, cursor, bytes.Length - cursor);
        return new(output.ToArray(), inventory, slots.Created, slots.Existing);
    }
}
