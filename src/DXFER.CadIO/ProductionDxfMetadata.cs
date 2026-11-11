using System.Globalization;
using HMISync.Web.Infrastructure;
using HMISync.Web.Production;
using static HMISync.Web.Infrastructure.DxfCompatibility;

namespace DXFER.CadIO;

// The geometry writer does not round-trip application objects. Only recognized,
// owned metadata may be detached; an arbitrary scalar XRECORD is not proof of safety.
internal static class ProductionDxfMetadata
{
    internal static byte[] PrepareWorkingCopy(byte[] source, IReadOnlyList<Pair> pairs, CancellationToken cancellationToken = default) =>
        new Inspection(source, pairs, cancellationToken).Prepare();

    private sealed record Record(string Type, string Section, int Start, int End, Pair[] Fields)
    {
        // SORTENTSTABLE repeats group 5 in its body for draw-order keys, not object IDs.
        public IEnumerable<Pair> IdentityFields => Type == "SORTENTSTABLE" ? Fields.TakeWhile(p => p.Code != 100) : Fields;
        public string? Handle => IdentityFields.FirstOrDefault(p => p.Code is 5 or 105)?.Value;
    }

    private sealed record Header(string? Owner, string? Extension, Pair[] ExtensionPairs, Pair[] Reactors, Pair[] Body);

    private sealed class Inspection(byte[] source, IReadOnlyList<Pair> pairs, CancellationToken cancellationToken)
    {
        private readonly List<Record> records = [];
        private readonly Dictionary<string, Record> handles = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Record, Header> headers = [];
        private readonly HashSet<Record> inspected = [];
        private readonly HashSet<Record> removed = [];
        private readonly HashSet<int> removalReferences = [];
        private readonly List<(int Start, int End)> spans = [];

        public byte[] Prepare()
        {
            ReadRecords();
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (record.Type is "SECTION" or "ENDSEC" or "EOF" or "CLASS" or "ENDTAB")
                {
                    Require(!record.Fields.Any(p => p.Code is 102 or >= 1000));
                    continue;
                }
                if (record.Section is "ENTITIES" or "BLOCKS") InspectEntity(record);
                else if (record.Section == "TABLES") InspectTable(record);
                else if (record.Section != "OBJECTS") Require(!record.Fields.Any(p => p.Code is 102 or >= 1000));
            }

            foreach (var root in records.Where(r => r.Section == "OBJECTS" && r.Type == "DICTIONARY" && ReadHeader(r).Owner == "0"))
                InspectDictionary(root, "0", "root");
            Require(records.Where(r => r.Section == "OBJECTS" && r.Type is not ("SECTION" or "ENDSEC"))
                .All(inspected.Contains));

            // Check inbound links across the entire file before deleting owned objects.
            // A second consumer, including a soft owner or XDATA handle, must not dangle.
            foreach (var record in records)
            foreach (var pair in record.Fields.Where(p => IsReference(p.Code)))
            {
                if (pair.Value == "0") continue;
                Require(handles.TryGetValue(pair.Value, out var target));
                if (removed.Contains(target!) || target!.Section is "ENTITIES" or "BLOCKS")
                    Require(removalReferences.Contains(pair.Start));
                else Require(target.Section == "TABLES" || inspected.Contains(target));
            }

            if (spans.Count == 0) return source;
            using var output = new MemoryStream(source.Length);
            var cursor = 0;
            foreach (var span in spans.OrderBy(s => s.Start))
            {
                Require(span.Start >= cursor);
                output.Write(source, cursor, span.Start - cursor);
                cursor = span.End;
            }
            output.Write(source, cursor, source.Length - cursor);
            return output.ToArray();
        }

        private void ReadRecords()
        {
            var section = "";
            for (var index = 0; index < pairs.Count;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Require(pairs[index].Code == 0);
                var end = index + 1;
                while (end < pairs.Count && pairs[end].Code != 0) end++;
                var fields = pairs.Skip(index + 1).Take(end - index - 1).ToArray();
                var type = pairs[index].Value.ToUpperInvariant();
                if (type == "SECTION")
                {
                    Require(section == "" && fields.Length > 0 && fields[0].Code == 2);
                    section = fields[0].Value.ToUpperInvariant();
                }
                var record = new Record(type, section, pairs[index].Start, end < pairs.Count ? pairs[end].Start : source.Length, fields);
                records.Add(record);
                if (type is not ("SECTION" or "ENDSEC" or "EOF" or "CLASS" or "ENDTAB"))
                {
                    Require(record.IdentityFields.Count(p => p.Code is 5 or 105) <= 1);
                    if (record.Handle is { } handle) Require(handle.Length > 0 && handles.TryAdd(handle, record));
                }
                if (type == "ENDSEC") section = "";
                index = end;
            }
        }

        private Header ReadHeader(Record record)
        {
            if (headers.TryGetValue(record, out var cached)) return cached;
            var fields = record.Fields;
            var bodyStart = Array.FindIndex(fields, p => p.Code == 100);
            if (bodyStart < 0) bodyStart = fields.Length;
            string? owner = null, extension = null;
            Pair[] extensionPairs = [], reactors = [];
            for (var index = 0; index < bodyStart; index++)
            {
                var pair = fields[index];
                if (pair.Code == 330) { Require(owner is null); owner = pair.Value; }
                if (pair.Code != 102) continue;
                Require(index + 2 < bodyStart && fields[index + 2].Code == 102 && fields[index + 2].Value == "}");
                var link = fields[index + 1];
                if (pair.Value == "{ACAD_XDICTIONARY")
                {
                    Require(extension is null && link.Code == 360);
                    extension = link.Value;
                    extensionPairs = fields[index..(index + 3)];
                }
                else
                {
                    Require(pair.Value == "{ACAD_REACTORS" && reactors.Length == 0 && link.Code == 330);
                    reactors = [link];
                }
                index += 2;
            }
            var header = new Header(owner, extension, extensionPairs, reactors, fields[bodyStart..]);
            headers.Add(record, header);
            return header;
        }

        private void InspectEntity(Record entity)
        {
            var header = ReadHeader(entity);
            Require(header.Reactors.Length == 0 && !entity.Fields.Any(p => p.Code >= 1000)
                && !header.Body.Any(p => p.Code == 102));
            // Other entity handles are not merely drawing ownership or the approved extension.
            Require(entity.Fields.Where(p => IsReference(p.Code)).All(p => p.Code == 330 || header.ExtensionPairs.Contains(p)));
            if (header.Extension is null) return;
            Require(entity.Handle is not null && entity.Section == "ENTITIES");
            var dictionary = Resolve(header.Extension, "DICTIONARY");
            Own(dictionary, entity.Handle!);
            Remove(dictionary);
            removalReferences.Add(header.ExtensionPairs[1].Start);
            var last = header.ExtensionPairs[^1];
            var next = Array.IndexOf(entity.Fields, last) + 1;
            spans.Add((header.ExtensionPairs[0].Start, next < entity.Fields.Length ? entity.Fields[next].Start : entity.End));
            foreach (var (key, leaf) in Entries(dictionary))
            {
                Require(leaf.Type == "XRECORD");
                Own(leaf, dictionary.Handle!);
                var payload = XrecordPayload(leaf);
                Require(payload.Length == 1);
                if (key is "btDetId" or "btPartId" or "btUnflattenedPartId" or "btOwnerFaceId_")
                    Require(payload[0].Code == 1000 && payload[0].Value.Length > 0);
                else if (key is "btBendAngle" or "btBendRadius" or "btBendIsUp" or "btIsBend")
                {
                    // These scalar annotations describe lines already removed by the
                    // shared bend-layer policy; they are not retained design constraints.
                    var layers = entity.Fields.Where(p => p.Code == 8).ToArray();
                    Require(entity.Type == "LINE" && layers.Length == 1 && BatchDxfLayers.Bend(layers[0].Value));
                    var value = payload[0];
                    Require(key switch
                    {
                        "btBendAngle" => value.Code == 40 && Number(value.Value, out _),
                        "btBendRadius" => value.Code == 40 && Number(value.Value, out var radius) && radius >= 0,
                        "btBendIsUp" => value.Code == 290 && value.Value is "0" or "1",
                        "btIsBend" => value.Code == 290 && value.Value == "1",
                        _ => false
                    });
                }
                else
                {
                    Require(key == "btArcOnSurfaceRadius" && entity.Type is "ARC" or "CIRCLE" && payload[0].Code == 40);
                    var radius = entity.Fields.Where(p => p.Code == 40).ToArray();
                    Require(radius.Length == 1 && Number(payload[0].Value, out var metadataRadius)
                        && Number(radius[0].Value, out var geometryRadius) && metadataRadius > 0 && metadataRadius == geometryRadius);
                }
                Remove(leaf);
            }
        }

        private void InspectTable(Record record)
        {
            Require(record.Type is "TABLE" or "VPORT" or "LTYPE" or "LAYER" or "STYLE" or "VIEW" or "UCS" or "APPID" or "DIMSTYLE" or "BLOCK_RECORD");
            var header = ReadHeader(record);
            Require(header.Reactors.Length == 0 && !header.Body.Any(p => p.Code == 102));
            InspectXdata(record);
            if (header.Extension is null) return;
            var role = record.Type switch
            {
                "TABLE" when record.Fields.Any(p => p.Code == 2 && p.Value == "VPORT") => "roundtrip",
                "BLOCK_RECORD" when record.Fields.Any(p => p.Code == 2 && p.Value is "*Model_Space" or "*Paper_Space" or "*Paper_Space0") => "sort",
                _ => "unsupported"
            };
            Require(role != "unsupported" && record.Handle is not null);
            InspectDictionary(Resolve(header.Extension, "DICTIONARY"), record.Handle!, role);
        }

        private void Own(Record record, string owner)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(record.Section == "OBJECTS" && record.Handle is not null && inspected.Add(record));
            var header = ReadHeader(record);
            Require(string.Equals(header.Owner, owner, StringComparison.OrdinalIgnoreCase)
                && header.Reactors.All(p => string.Equals(p.Value, owner, StringComparison.OrdinalIgnoreCase)));
            Require(record.Fields.Take(record.Fields.Length - header.Body.Length)
                .All(p => p.Code is 5 or 330 or 102 || header.ExtensionPairs.Contains(p)));
        }

        private void Remove(Record record)
        {
            removed.Add(record);
            spans.Add((record.Start, record.End));
            foreach (var reference in record.Fields.Where(p => IsReference(p.Code))) removalReferences.Add(reference.Start);
        }

        private IEnumerable<(string Key, Record Record)> Entries(Record dictionary)
        {
            var header = ReadHeader(dictionary);
            Require(header.Extension is null);
            var fields = header.Body;
            Require(fields.Length > 0 && fields[0].Code == 100 && fields[0].Value == "AcDbDictionary");
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var flags = new HashSet<int>();
            for (var index = 1; index < fields.Length; index++)
            {
                var pair = fields[index];
                if (pair.Code is 280 or 281)
                {
                    Require(flags.Add(pair.Code) && int.TryParse(pair.Value, out var flag) && flag >= 0 && flag <= (pair.Code == 280 ? 1 : 5));
                    continue;
                }
                if (dictionary.Type == "ACDBDICTIONARYWDFLT" && pair.Code == 100 && pair.Value == "AcDbDictionaryWithDefault") continue;
                if (dictionary.Type == "ACDBDICTIONARYWDFLT" && pair.Code == 340) continue;
                Require(pair.Code == 3 && pair.Value.Length > 0 && keys.Add(pair.Value) && index + 1 < fields.Length);
                var link = fields[++index];
                Require(link.Code is 350 or 360 && handles.ContainsKey(link.Value));
                yield return (pair.Value, handles[link.Value]);
            }
        }

        private void InspectDictionary(Record dictionary, string owner, string role)
        {
            Own(dictionary, owner);
            foreach (var (key, child) in Entries(dictionary))
            {
                var childRole = role switch
                {
                    "root" => key switch
                    {
                        "ACAD_GROUP" => "empty",
                        "ACAD_LAYOUT" => "collection:LAYOUT",
                        "ACAD_MATERIAL" => "collection:MATERIAL",
                        "ACAD_MLEADERSTYLE" => "collection:MLEADERSTYLE",
                        "ACAD_MLINESTYLE" => "collection:MLINESTYLE",
                        "ACAD_PLOTSETTINGS" => "collection:PLOTSETTINGS",
                        "ACAD_PLOTSTYLENAME" => "collection:ACDBPLACEHOLDER",
                        "ACAD_SCALELIST" => "collection:SCALE",
                        "ACAD_TABLESTYLE" => "collection:TABLESTYLE",
                        "ACAD_VISUALSTYLE" => "collection:VISUALSTYLE",
                        "AcDbVariableDictionary" => "variables",
                        "DWGPROPS" => "XRECORD",
                        "ONSHAPE_DATA" => "onshape",
                        _ => "unsupported"
                    },
                    "roundtrip" when key == "ACAD_XREC_ROUNDTRIP" => "XRECORD",
                    "sort" when key == "ACAD_SORTENTS" => "SORTENTSTABLE",
                    "variables" when key is "CANNOSCALE" or "CMLEADERSTYLE" or "CTABLESTYLE" or "DIMASSOC" or "HIDETEXT" or "MSLTSCALE" => "DICTIONARYVAR",
                    "onshape" when key is "DIM_UNITS" or "DWGGEN_VERSION" or "FIRST_ANGLE" => "XRECORD",
                    _ when role.StartsWith("collection:", StringComparison.Ordinal) => role[11..],
                    _ => "unsupported"
                };
                Require(childRole != "unsupported");
                if (child.Type is "DICTIONARY" or "ACDBDICTIONARYWDFLT")
                {
                    Require(childRole is "empty" or "variables" or "onshape" || childRole.StartsWith("collection:", StringComparison.Ordinal));
                    InspectDictionary(child, dictionary.Handle!, childRole);
                }
                else
                {
                    Require(child.Type == childRole);
                    Own(child, dictionary.Handle!);
                    InspectMetadataObject(child);
                }
            }
        }

        private void InspectMetadataObject(Record record)
        {
            var header = ReadHeader(record);
            if (record.Type == "SORTENTSTABLE") InspectSortOrder(record);
            else if (record.Type == "XRECORD")
            {
                Require(header.Extension is null);
                foreach (var pair in XrecordPayload(record))
                    Require(IsMetadataScalar(pair) || IsReference(pair.Code));
            }
            else
            {
                Require(!header.Body.Any(p => p.Code == 102 || p.Code is >= 310 and <= 319));
                InspectXdata(record);
                if (header.Extension is not null)
                {
                    Require(record.Type is "MATERIAL" or "MLEADERSTYLE" or "VISUALSTYLE");
                    InspectDictionary(Resolve(header.Extension, "DICTIONARY"), record.Handle!, "roundtrip");
                }
            }
        }

        private void InspectSortOrder(Record record)
        {
            var header = ReadHeader(record);
            Require(header.Extension is null && header.Owner is not null);
            var dictionary = Resolve(header.Owner!, "DICTIONARY");
            var blockHandle = ReadHeader(dictionary).Owner;
            Require(blockHandle is not null);
            var block = Resolve(blockHandle!, "BLOCK_RECORD");
            Require(block.Section == "TABLES" && string.Equals(ReadHeader(block).Extension, dictionary.Handle, StringComparison.OrdinalIgnoreCase));
            var body = header.Body;
            Require(body.Length >= 2 && body[0].Code == 100 && body[0].Value == "AcDbSortentsTable"
                && body[1].Code == 330 && string.Equals(body[1].Value, block.Handle, StringComparison.OrdinalIgnoreCase)
                && (body.Length - 2) % 2 == 0);
            var entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sortHandles = new HashSet<ulong>();
            for (var index = 2; index < body.Length; index += 2)
            {
                var reference = body[index];
                var sort = body[index + 1];
                Require(reference.Code == 331 && sort.Code == 5 && entities.Add(reference.Value)
                    && ulong.TryParse(sort.Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var order)
                    && order != 0 && sortHandles.Add(order));
                Require(handles.TryGetValue(reference.Value, out var entity) && entity.Section == "ENTITIES"
                    && string.Equals(ReadHeader(entity).Owner, block.Handle, StringComparison.OrdinalIgnoreCase));
                removalReferences.Add(reference.Start);
            }
            // Verified ordering links are not design constraints. Drop only these entries
            // so subsequent hole/duplicate cleanup cannot leave dangling draw-order links.
            if (body.Length > 2) spans.Add((body[2].Start, record.End));
        }

        private static Pair[] XrecordPayload(Record record)
        {
            var fields = record.Fields;
            Require(fields.Count(p => p.Code == 100) == 1);
            var start = Array.FindIndex(fields, p => p.Code == 100 && p.Value == "AcDbXrecord");
            Require(start >= 0 && start + 1 < fields.Length && fields[start + 1].Code == 280
                && int.TryParse(fields[start + 1].Value, out var flag) && flag is >= 0 and <= 5);
            return fields[(start + 2)..];
        }

        private static void InspectXdata(Record record)
        {
            var data = record.Fields.Where(p => p.Code >= 1000).ToArray();
            if (data.Length == 0) return;
            var font = record.Type == "STYLE" && data.Length == 3 && data[0].Code == 1001 && data[0].Value == "ACAD"
                && data[1].Code == 1000 && data[2].Code == 1071 && int.TryParse(data[2].Value, out _);
            var visual = record.Type == "VISUALSTYLE" && data.Length == 3 && data[0].Code == 1001 && data[0].Value == "ACAD"
                && data[1].Code == 1000 && data[1].Value == "AcDbSavedByObjectVersion" && data[2].Code == 1070 && short.TryParse(data[2].Value, out _);
            var leader = record.Type == "MLEADERSTYLE" && data.Length == 2 && data[0].Code == 1001 && data[0].Value == "ACAD_MLEADERVER"
                && data[1].Code == 1070 && short.TryParse(data[1].Value, out _);
            Require(font || visual || leader);
        }

        private Record Resolve(string handle, string type)
        {
            Require(handles.TryGetValue(handle, out var record) && record.Type == type);
            return record!;
        }
    }

    private static bool IsReference(int code) => code is >= 320 and <= 369 or >= 390 and <= 399 or 1005;
    private static bool Number(string value, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

    private static bool IsMetadataScalar(Pair pair) => pair.Code switch
    {
        >= 1 and <= 4 or >= 6 and <= 9 or >= 300 and <= 309 or 1000 => true,
        102 => !pair.Value.StartsWith('{') && pair.Value != "}",
        >= 40 and <= 59 or >= 140 and <= 149 or 1040 => Number(pair.Value, out _),
        >= 60 and <= 99 or >= 170 and <= 179 or >= 270 and <= 299 or 1070 or 1071 => long.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        _ => false
    };

    private static void Require(bool condition)
    {
        if (!condition)
            throw new ProductionDxfPreparationException("Production cleanup requires review: associated DXF records or application data cannot be discarded safely. Original file unchanged.");
    }
}
