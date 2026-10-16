using System.Text.Json;
using DXFER.Core.Documents;

namespace DXFER.Core.Operations;

public static class DrawingLayerService
{
    public static string LayerName(DrawingDocument document, string id) =>
        document.Metadata.EntityStyles.TryGetValue(id, out var style) && !string.IsNullOrWhiteSpace(style.LayerName)
            ? style.LayerName : "0";

    public static IReadOnlyDictionary<string, DxfEntityStyle> GetLayers(DrawingDocument document)
    {
        var result = new Dictionary<string, DxfEntityStyle>(StringComparer.OrdinalIgnoreCase) { ["0"] = new("0", "CONTINUOUS", 7) };
        foreach (var style in document.Entities.Select(e => document.Metadata.EntityStyles.GetValueOrDefault(e.Id.Value)).OfType<DxfEntityStyle>())
            if (!string.IsNullOrWhiteSpace(style.LayerName) && !result.ContainsKey(style.LayerName))
                result[style.LayerName] = new(style.LayerName, style.LineTypeName, style.ColorNumber ?? 7, style.TrueColor);
        foreach (var pair in document.Metadata.Layers) result[pair.Key] = pair.Value with { LayerName = pair.Key };
        return result;
    }

    public static DxfEntityStyle Layer(DrawingDocument document, string id) => GetLayers(document)[LayerName(document, id)];
    public static bool CanEdit(DrawingDocument document, string id)
    {
        var layer = Layer(document, id);
        return !layer.IsHidden && !layer.IsLocked;
    }

    public static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name == name.Trim()
        && name.Length <= 255 && !name.Any(c => char.IsControl(c) || "<>/\\\":;?*|=,".Contains(c));

    public static DrawingDocument SetLayer(DrawingDocument document, DxfEntityStyle layer, string? previousName = null)
    {
        var name = layer.LayerName;
        if (!ValidName(name) || layer.TrueColor is < 0 or > 0xFFFFFF) throw new ArgumentException("Enter a valid, unique layer name and color.");
        var layers = new Dictionary<string, DxfEntityStyle>(GetLayers(document), StringComparer.OrdinalIgnoreCase);
        var styles = new Dictionary<string, DxfEntityStyle>(document.Metadata.EntityStyles, StringComparer.Ordinal);
        var active = document.Metadata.ActiveLayerName;
        if (previousName is not null && !string.Equals(previousName, name, StringComparison.Ordinal))
        {
            if (previousName == "0") throw new ArgumentException("Layer 0 cannot be renamed.");
            if (layers.ContainsKey(name!) && !name!.Equals(previousName, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("That layer name already exists.");
            if (!layers.Remove(previousName)) throw new ArgumentException("Layer no longer exists.");
            foreach (var entity in document.Entities.Where(e => LayerName(document, e.Id.Value).Equals(previousName, StringComparison.OrdinalIgnoreCase)))
                styles[entity.Id.Value] = styles.GetValueOrDefault(entity.Id.Value, new()) with { LayerName = name };
            if (active.Equals(previousName, StringComparison.OrdinalIgnoreCase)) active = name!;
        }
        else if (previousName is null && layers.ContainsKey(name!)) throw new ArgumentException("That layer name already exists.");
        layers[name!] = layer;
        if (previousName is not null && layer.TrueColor != GetLayers(document)[previousName].TrueColor)
            foreach (var entity in document.Entities.Where(e => LayerName(document, e.Id.Value).Equals(previousName, StringComparison.OrdinalIgnoreCase)))
                styles[entity.Id.Value] = styles.GetValueOrDefault(entity.Id.Value, new(name)) with { ColorNumber = 256, TrueColor = null };
        return WithMetadata(document, document.Metadata with { Layers = layers, EntityStyles = styles, ActiveLayerName = active });
    }

    public static DrawingDocument DeleteEmpty(DrawingDocument document, string name)
    {
        if (name == "0" || name.Equals(document.Metadata.ActiveLayerName, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The default or current drawing layer cannot be deleted.");
        if (document.Entities.Any(e => LayerName(document, e.Id.Value).Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Move the layer's entities before deleting it.");
        var layers = new Dictionary<string, DxfEntityStyle>(GetLayers(document), StringComparer.OrdinalIgnoreCase);
        layers.Remove(name);
        return WithMetadata(document, document.Metadata with { Layers = layers });
    }

    public static DrawingDocument Move(DrawingDocument document, IEnumerable<string> ids, string name)
    {
        var target = GetLayers(document)[name];
        if (target.IsHidden || target.IsLocked) throw new ArgumentException("Show and unlock the destination layer first.");
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        var styles = new Dictionary<string, DxfEntityStyle>(document.Metadata.EntityStyles, StringComparer.Ordinal);
        foreach (var entity in document.Entities.Where(e => selected.Contains(e.Id.Value)))
        {
            if (!CanEdit(document, entity.Id.Value)) throw new ArgumentException("Show and unlock selected layers first.");
            styles[entity.Id.Value] = styles.GetValueOrDefault(entity.Id.Value, new()) with { LayerName = name, ColorNumber = 256, TrueColor = null };
        }
        return WithMetadata(document, document.Metadata with { EntityStyles = styles });
    }

    public static DrawingDocument AssignNewEntities(DrawingDocument before, DrawingDocument after)
    {
        var layers = GetLayers(after);
        var name = after.Metadata.ActiveLayerName;
        if (!layers.TryGetValue(name, out var layer)) layer = layers[name = "0"];
        var oldIds = before.Entities.Select(e => e.Id.Value).ToHashSet();
        var added = after.Entities.Where(e => !oldIds.Contains(e.Id.Value) && !after.Metadata.EntityStyles.ContainsKey(e.Id.Value)).ToArray();
        if (added.Length == 0) return after;
        if (layer.IsHidden || layer.IsLocked) throw new ArgumentException("Show and unlock the current drawing layer before adding geometry.");
        var styles = new Dictionary<string, DxfEntityStyle>(after.Metadata.EntityStyles, StringComparer.Ordinal);
        foreach (var e in added) styles[e.Id.Value] = new(name, ColorNumber: 256);
        return WithMetadata(after, after.Metadata with { EntityStyles = styles });
    }

    public static bool PreservesProtectedGeometry(DrawingDocument before, DrawingDocument after)
    {
        var layers = GetLayers(before);
        var next = after.Entities.ToDictionary(e => e.Id.Value);
        var protectedIds = before.Entities.Where(e => layers[LayerName(before, e.Id.Value)] is { IsHidden: true } or { IsLocked: true }).Select(e => e.Id.Value).ToHashSet();
        foreach (var e in before.Entities.Where(e => protectedIds.Contains(e.Id.Value)))
            if (!next.TryGetValue(e.Id.Value, out var other) || e.GetType() != other.GetType()
                || JsonSerializer.Serialize(e, e.GetType()) != JsonSerializer.Serialize(other, other.GetType())) return false;
        foreach (var d in before.Dimensions.Where(d => d.ReferenceKeys.Any(r => protectedIds.Contains(r.Split('|')[0]))))
            if (JsonSerializer.Serialize(d) != JsonSerializer.Serialize(after.Dimensions.FirstOrDefault(n => n.Id == d.Id))) return false;
        foreach (var c in before.Constraints.Where(c => c.ReferenceKeys.Any(r => protectedIds.Contains(r.Split('|')[0]))))
            if (JsonSerializer.Serialize(c) != JsonSerializer.Serialize(after.Constraints.FirstOrDefault(n => n.Id == c.Id))) return false;
        return true;
    }

    public static string Color(DxfEntityStyle layer)
    {
        if (layer.TrueColor is { } rgb) return $"#{rgb:X6}";
        var index = Math.Abs((long)(layer.ColorNumber ?? 7));
        return index is 0 or 7 or > 255 ? "#D3E1ED" : $"#{IndexedColors[index]:X6}";
    }

    // Standard DXF indexed colors, matching ezdxf's default ACI table. Index 7
    // follows the application's background contrast; true-color values do not.
    private static readonly int[] IndexedColors =
    {
        0x000000, 0xFF0000, 0xFFFF00, 0x00FF00, 0x00FFFF, 0x0000FF, 0xFF00FF, 0xFFFFFF, 0x808080, 0xC0C0C0,
        0xFF0000, 0xFF7F7F, 0xA50000, 0xA55252, 0x7F0000, 0x7F3F3F, 0x4C0000, 0x4C2626, 0x260000, 0x261313,
        0xFF3F00, 0xFF9F7F, 0xA52900, 0xA56752, 0x7F1F00, 0x7F4F3F, 0x4C1300, 0x4C2F26, 0x260900, 0x261713,
        0xFF7F00, 0xFFBF7F, 0xA55200, 0xA57C52, 0x7F3F00, 0x7F5F3F, 0x4C2600, 0x4C3926, 0x261300, 0x261C13,
        0xFFBF00, 0xFFDF7F, 0xA57C00, 0xA59152, 0x7F5F00, 0x7F6F3F, 0x4C3900, 0x4C4226, 0x261C00, 0x262113,
        0xFFFF00, 0xFFFF7F, 0xA5A500, 0xA5A552, 0x7F7F00, 0x7F7F3F, 0x4C4C00, 0x4C4C26, 0x262600, 0x262613,
        0xBFFF00, 0xDFFF7F, 0x7CA500, 0x91A552, 0x5F7F00, 0x6F7F3F, 0x394C00, 0x424C26, 0x1C2600, 0x212613,
        0x7FFF00, 0xBFFF7F, 0x52A500, 0x7CA552, 0x3F7F00, 0x5F7F3F, 0x264C00, 0x394C26, 0x132600, 0x1C2613,
        0x3FFF00, 0x9FFF7F, 0x29A500, 0x67A552, 0x1F7F00, 0x4F7F3F, 0x134C00, 0x2F4C26, 0x092600, 0x172613,
        0x00FF00, 0x7FFF7F, 0x00A500, 0x52A552, 0x007F00, 0x3F7F3F, 0x004C00, 0x264C26, 0x002600, 0x132613,
        0x00FF3F, 0x7FFF9F, 0x00A529, 0x52A567, 0x007F1F, 0x3F7F4F, 0x004C13, 0x264C2F, 0x002609, 0x135817,
        0x00FF7F, 0x7FFFBF, 0x00A552, 0x52A57C, 0x007F3F, 0x3F7F5F, 0x004C26, 0x264C39, 0x002613, 0x13581C,
        0x00FFBF, 0x7FFFDF, 0x00A57C, 0x52A591, 0x007F5F, 0x3F7F6F, 0x004C39, 0x264C42, 0x00261C, 0x135858,
        0x00FFFF, 0x7FFFFF, 0x00A5A5, 0x52A5A5, 0x007F7F, 0x3F7F7F, 0x004C4C, 0x264C4C, 0x002626, 0x135858,
        0x00BFFF, 0x7FDFFF, 0x007CA5, 0x5291A5, 0x005F7F, 0x3F6F7F, 0x00394C, 0x26427E, 0x001C26, 0x135858,
        0x007FFF, 0x7FBFFF, 0x0052A5, 0x527CA5, 0x003F7F, 0x3F5F7F, 0x00264C, 0x26397E, 0x001326, 0x131C58,
        0x003FFF, 0x7F9FFF, 0x0029A5, 0x5267A5, 0x001F7F, 0x3F4F7F, 0x00134C, 0x262F7E, 0x000926, 0x131758,
        0x0000FF, 0x7F7FFF, 0x0000A5, 0x5252A5, 0x00007F, 0x3F3F7F, 0x00004C, 0x26267E, 0x000026, 0x131358,
        0x3F00FF, 0x9F7FFF, 0x2900A5, 0x6752A5, 0x1F007F, 0x4F3F7F, 0x13004C, 0x2F267E, 0x090026, 0x171358,
        0x7F00FF, 0xBF7FFF, 0x5200A5, 0x7C52A5, 0x3F007F, 0x5F3F7F, 0x26004C, 0x39267E, 0x130026, 0x1C1358,
        0xBF00FF, 0xDF7FFF, 0x7C00A5, 0x9152A5, 0x5F007F, 0x6F3F7F, 0x39004C, 0x42264C, 0x1C0026, 0x581358,
        0xFF00FF, 0xFF7FFF, 0xA500A5, 0xA552A5, 0x7F007F, 0x7F3F7F, 0x4C004C, 0x4C264C, 0x260026, 0x581358,
        0xFF00BF, 0xFF7FDF, 0xA5007C, 0xA55291, 0x7F005F, 0x7F3F6F, 0x4C0039, 0x4C2642, 0x26001C, 0x581358,
        0xFF007F, 0xFF7FBF, 0xA50052, 0xA5527C, 0x7F003F, 0x7F3F5F, 0x4C0026, 0x4C2639, 0x260013, 0x58131C,
        0xFF003F, 0xFF7F9F, 0xA50029, 0xA55267, 0x7F001F, 0x7F3F4F, 0x4C0013, 0x4C262F, 0x260009, 0x581317,
        0x000000, 0x656565, 0x666666, 0x999999, 0xCCCCCC, 0xFFFFFF
    };

    public static DrawingDocument WithMetadata(DrawingDocument document, DrawingDocumentMetadata metadata) =>
        new(document.Entities, document.Dimensions, document.Constraints, metadata);
}
