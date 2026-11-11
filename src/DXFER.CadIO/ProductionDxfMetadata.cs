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
        // Group 5 is a legacy arrow name in DIMSTYLE, and a draw-order key in
        // SORTENTSTABLE's body. Neither is another object identity.
        public IEnumerable<Pair> IdentityFields => Type switch
        {
            "DIMSTYLE" => Fields.Where(p => p.Code != 5),
            "SORTENTSTABLE" => Fields.TakeWhile(p => p.Code != 100),
            _ => Fields
        };
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
        private bool legacyXrecords;

        public byte[] Prepare()
        {
            ReadRecords();
            var fileHeaders = records.Where(r => r.Type == "SECTION" && r.Section == "HEADER").ToArray();
            var headerFields = fileHeaders.SelectMany(r => r.Fields).ToArray();
            var versions = headerFields.Select((pair, index) => (pair, index))
                .Where(item => item.pair.Code == 9 && item.pair.Value == "$ACADVER").ToArray();
            legacyXrecords = fileHeaders.Length == 1 && versions.Length == 1 && versions[0].index + 1 < headerFields.Length
                && headerFields[versions[0].index + 1] is { Code: 1, Value: "AC1012" or "AC1014" };
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
                if (leaf.Type == "ACAD_PROXY_OBJECT")
                {
                    Require(key is "BTBENDISUP" or "BTISBEND" && IsDiscardedBend(entity));
                    Own(leaf, dictionary.Handle!);
                    InspectLegacyProxy(leaf, "XRECORD");
                    Require(!ReadHeader(leaf).Body.Any(p => IsReference(p.Code)));
                    Remove(leaf);
                    continue;
                }
                Require(leaf.Type == "XRECORD");
                Own(leaf, dictionary.Handle!);
                var payload = XrecordPayload(leaf);
                Require(payload.Length == 1);
                if (key is "BTDETID" or "BTPARTID" or "BTUNFLATTENEDPARTID" or "BTOWNERFACEID_")
                    Require(payload[0].Code == 1000 && payload[0].Value.Length > 0);
                else if (key is "BTBENDANGLE" or "BTBENDRADIUS" or "BTBENDISUP" or "BTISBEND")
                {
                    // These scalar annotations describe lines already removed by the
                    // shared bend-layer policy; they are not retained design constraints.
                    Require(IsDiscardedBend(entity));
                    var value = payload[0];
                    Require(key switch
                    {
                        "BTBENDANGLE" => value.Code == 40 && Number(value.Value, out _),
                        "BTBENDRADIUS" => value.Code == 40 && Number(value.Value, out var radius) && radius >= 0,
                        "BTBENDISUP" => value.Code == 290 && value.Value is "0" or "1",
                        "BTISBEND" => value.Code == 290 && value.Value == "1",
                        _ => false
                    });
                }
                else
                {
                    Require(key == "BTARCONSURFACERADIUS" && entity.Type is "ARC" or "CIRCLE" && payload[0].Code == 40);
                    var radius = entity.Fields.Where(p => p.Code == 40).ToArray();
                    Require(radius.Length == 1 && Number(payload[0].Value, out var metadataRadius)
                        && Number(radius[0].Value, out var geometryRadius) && metadataRadius > 0 && metadataRadius == geometryRadius);
                }
                Remove(leaf);
            }
        }

        private static bool IsDiscardedBend(Record entity)
        {
            var layers = entity.Fields.Where(p => p.Code == 8).ToArray();
            return entity.Type == "LINE" && layers.Length == 1 && BatchDxfLayers.Bend(layers[0].Value);
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
                "TABLE" when record.Fields.Any(p => p.Code == 2 && p.Value.Equals("VPORT", StringComparison.OrdinalIgnoreCase)) => "roundtrip",
                "BLOCK_RECORD" when record.Fields.Any(p => p.Code == 2 && p.Value.ToUpperInvariant() is "*MODEL_SPACE" or "*PAPER_SPACE" or "*PAPER_SPACE0") => "sort",
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
                // Exporters vary the casing of known dictionary keys. Payload values
                // and ownership checks remain unchanged.
                yield return (pair.Value.ToUpperInvariant(), handles[link.Value]);
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
                        "ACDBVARIABLEDICTIONARY" => "variables",
                        "ACDBHEADERROUNDTRIPXREC" when legacyXrecords => "legacy-header",
                        "DWGPROPS" => "XRECORD",
                        "ONSHAPE_DATA" => "onshape",
                        _ => "unsupported"
                    },
                    "roundtrip" when key == "ACAD_XREC_ROUNDTRIP" => "XRECORD",
                    "sort" when key == "ACAD_SORTENTS" => "SORTENTSTABLE",
                    "variables" when key is "CANNOSCALE" or "CMLEADERSTYLE" or "CTABLESTYLE" or "DIMASSOC" or "HIDETEXT" or "MSLTSCALE" => "DICTIONARYVAR",
                    "onshape" when key is "DIM_UNITS" or "DWGGEN_VERSION" or "FIRST_ANGLE" => "XRECORD",
                    "legacy-header" => "legacy-header-value",
                    _ when role.StartsWith("collection:", StringComparison.Ordinal) => role[11..],
                    _ => "unsupported"
                };
                Require(childRole != "unsupported");
                if (child.Type is "DICTIONARY" or "ACDBDICTIONARYWDFLT")
                {
                    Require(childRole is "empty" or "variables" or "onshape" or "legacy-header" || childRole.StartsWith("collection:", StringComparison.Ordinal));
                    InspectDictionary(child, dictionary.Handle!, childRole);
                }
                else if (childRole == "legacy-header-value")
                {
                    Require(child.Type == "XRECORD");
                    Own(child, dictionary.Handle!);
                    InspectLegacyHeaderValue(child, key);
                }
                else if (child.Type == "ACAD_PROXY_OBJECT")
                {
                    Require(childRole is "SCALE" or "VISUALSTYLE" or "MLEADERSTYLE"
                        || role == "onshape" && key == "FIRST_ANGLE");
                    Own(child, dictionary.Handle!);
                    InspectLegacyProxy(child, childRole);
                }
                else
                {
                    Require(child.Type == childRole);
                    Own(child, dictionary.Handle!);
                    InspectMetadataObject(child);
                }
            }
        }

        private void InspectLegacyHeaderValue(Record record, string key)
        {
            Require(legacyXrecords && ReadHeader(record).Extension is null);
            var payload = XrecordPayload(record);
            if (key == "INSUNITS")
            {
                Require(payload.Length == 1 && payload[0].Code == 90);
                if (payload[0].Value is not ("0" or "1"))
                    throw new ProductionDxfPreparationException("Production cleanup requires inch coordinates: legacy DXF insertion units require review; no scaling was guessed. Original file unchanged.");
                return;
            }
            if (key == "DIMSTYLEDATA")
            {
                Require(payload.Length == 5 && payload[0] is { Code: 1000, Value: "DSTYLE" }
                    && payload[1] is { Code: 1002, Value: "{" } && payload[2] is { Code: 1070, Value: "340" }
                    && payload[3].Code == 340 && payload[4] is { Code: 1002, Value: "}" });
                if (payload[3].Value != "0") Require(Resolve(payload[3].Value, "STYLE").Section == "TABLES");
                return;
            }
            var expectedCode = key switch
            {
                "CEPSNID" or "LAYOUTDICT" or "PLOTSETDICT" or "UCSBASE" or "PUCSBASE" or "UCSORTHOREF" or "PUCSORTHOREF" => 340,
                "CEPSNTYPE" or "LWETCUNION" or "TSTACKALIGN" or "TSTACKSIZE" => 90,
                "FINGERPRINTGUID" or "VERSIONGUID" or "HYPERLINKBASE" or "STYLESHEET" => 1,
                "PSVPSCALE" => 40,
                "UCSORTHOVIEW" or "PUCSORTHOVIEW" => 70,
                "UCSORGBACK" or "UCSORGBOTTOM" or "UCSORGFRONT" or "UCSORGLEFT" or "UCSORGRIGHT" or "UCSORGTOP"
                    or "PUCSORGBACK" or "PUCSORGBOTTOM" or "PUCSORGFRONT" or "PUCSORGLEFT" or "PUCSORGRIGHT" or "PUCSORGTOP" => 10,
                _ => -1
            };
            if (expectedCode == 10)
                Require(payload.Length == 3 && payload.Select(p => p.Code).SequenceEqual(new[] { 10, 20, 30 })
                    && payload.All(p => Number(p.Value, out _)));
            else
                Require(payload.Length == 1 && payload[0].Code == expectedCode
                    && (IsMetadataScalar(payload[0]) || IsReference(payload[0].Code)));
        }

        private void InspectLegacyProxy(Record record, string expectedType)
        {
            var header = ReadHeader(record);
            var body = header.Body;
            Require(legacyXrecords && header.Extension is null && body.Length >= 5
                && body[0] is { Code: 100, Value: "AcDbProxyObject" } && body[1] is { Code: 90, Value: "499" }
                && body[2].Code == 91 && body[3].Code == 93);
            Require(int.TryParse(body[2].Value, out var classId));
            var classes = records.Where(r => r.Section == "CLASSES" && r.Type == "CLASS").ToArray();
            Require(classId >= 500 && classId - 500 < classes.Length);
            var definition = classes[classId - 500];
            var className = expectedType switch
            {
                "XRECORD" => "AcDbXrecord", "SCALE" => "AcDbScale",
                "VISUALSTYLE" => "AcDbVisualStyle", "MLEADERSTYLE" => "AcDbMLeaderStyle", _ => ""
            };
            Require(className.Length > 0 && definition.Fields.Count(p => p.Code == 1) == 1
                && definition.Fields.Single(p => p.Code == 1).Value == expectedType
                && definition.Fields.Count(p => p.Code == 2) == 1 && definition.Fields.Single(p => p.Code == 2).Value == className
                && definition.Fields.Count(p => p.Code == 281) == 1 && definition.Fields.Single(p => p.Code == 281).Value == "0");
            Require(int.TryParse(body[3].Value, out var bits) && bits > 0);
            long byteCount = 0;
            var index = 4;
            for (; index < body.Length && body[index].Code == 310; index++)
            {
                var hex = body[index].Value;
                Require(hex.Length > 0 && hex.Length % 2 == 0 && hex.All(Uri.IsHexDigit));
                byteCount += hex.Length / 2;
            }
            Require(byteCount == ((long)bits + 7) / 8);
            var referencesStart = index;
            while (index < body.Length && body[index].Code is 330 or 340 or 350 or 360) index++;
            if (index > referencesStart || index < body.Length && body[index].Code == 94)
            {
                Require(index < body.Length && body[index] is { Code: 94, Value: "0" });
                index++;
            }
            if (index < body.Length && body[index].Code == 95)
            {
                Require(uint.TryParse(body[index].Value, out _));
                index++;
            }
            if (index < body.Length && body[index].Code == 70)
            {
                Require(body[index].Value is "0" or "1");
                index++;
            }
            Require(body[index..].All(p => p.Code >= 1000));
            InspectXdata(record, expectedType);
            // Known display metadata stays intact in the working copy. Only the
            // caller for discarded bend flags removes it; inbound links are checked later.
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

        private Pair[] XrecordPayload(Record record)
        {
            var fields = record.Fields;
            Require(fields.Count(p => p.Code == 100) == 1);
            var start = Array.FindIndex(fields, p => p.Code == 100 && p.Value == "AcDbXrecord");
            Require(start >= 0);
            var payloadStart = start + 1;
            if (payloadStart < fields.Length && fields[payloadStart].Code == 280)
            {
                Require(int.TryParse(fields[payloadStart].Value, out var flag) && flag is >= 0 and <= 5);
                payloadStart++;
            }
            // R13/R14 exporters omit the later cloning flag. Do not skip the first
            // payload field: it still needs the same scalar/reference validation.
            else Require(legacyXrecords);
            return fields[payloadStart..];
        }

        private static void InspectXdata(Record record, string? effectiveType = null)
        {
            var type = effectiveType ?? record.Type;
            var data = record.Fields.Where(p => p.Code >= 1000).ToArray();
            if (data.Length == 0) return;
            var font = type == "STYLE" && data.Length == 3 && data[0].Code == 1001 && data[0].Value == "ACAD"
                && data[1].Code == 1000 && data[2].Code == 1071 && int.TryParse(data[2].Value, out _);
            var visual = type == "VISUALSTYLE" && data.Length == 3 && data[0].Code == 1001 && data[0].Value == "ACAD"
                && data[1].Code == 1000 && data[1].Value == "AcDbSavedByObjectVersion" && data[2].Code == 1070 && short.TryParse(data[2].Value, out _);
            var leader = type == "MLEADERSTYLE" && data.Length == 2 && data[0].Code == 1001 && data[0].Value == "ACAD_MLEADERVER"
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
