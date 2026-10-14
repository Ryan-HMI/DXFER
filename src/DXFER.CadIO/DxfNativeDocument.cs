using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DXFER.Core.Documents;
using DXFER.Core.IO;
using static DXFER.CadIO.DxfDocumentReader;

namespace DXFER.CadIO;

// A named OBJECTS/XRECORD keeps application data out of manufacturing entities.
// Fingerprint all non-OBJECTS records, not just coordinates, before restoring any snapshot.
internal static class DxfNativeDocument
{
    private const string DictionaryKey = "DXFER_DOCUMENT";
    private const string Version = "DXFER_NATIVE_V1";
    private const string IgnoredCode = "native-document-ignored";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static string Embed(string dxf, DrawingDocument document)
    {
        var bytes = Utf8.GetBytes(DxferDocumentSerializer.Write(document));
        if (bytes.Length > DxferDocumentSerializer.MaxTextLength)
            throw new FormatException("Native document exceeds the embedded size limit.");
        var pairs = ReadPairs(dxf, new List<DrawingDocumentWarning>()).ToArray();
        var builder = new StringBuilder(dxf[..pairs[^1].Start]);
        Pair(builder, 0, "SECTION"); Pair(builder, 2, "OBJECTS");
        // Writer reserves 1..7 for space records and starts geometry/table handles at 100 hex.
        Pair(builder, 0, "DICTIONARY"); Pair(builder, 5, "8"); Pair(builder, 330, "0");
        Pair(builder, 100, "AcDbDictionary"); Pair(builder, 280, "0"); Pair(builder, 281, "1");
        Pair(builder, 3, DictionaryKey); Pair(builder, 350, "9");
        Pair(builder, 0, "XRECORD"); Pair(builder, 5, "9"); Pair(builder, 330, "8");
        Pair(builder, 100, "AcDbXrecord"); Pair(builder, 280, "1");
        Pair(builder, 1, Version); Pair(builder, 1, Fingerprint(pairs)); Pair(builder, 1, Hash(bytes));
        for (var offset = 0; offset < bytes.Length; offset += 127)
            Pair(builder, 310, Convert.ToHexString(bytes.AsSpan(offset, Math.Min(127, bytes.Length - offset))));
        Pair(builder, 0, "ENDSEC"); Pair(builder, 0, "EOF");
        return builder.ToString();
    }

    internal static DrawingDocument Restore(IReadOnlyList<DxfPair> pairs, DrawingDocument imported, bool malformedRecords, out bool restored)
    {
        restored = false;
        var objects = ObjectPairs(pairs).ToArray();
        if (!objects.Any(p => p.Code == 3 && p.Value == DictionaryKey
            || p.Code == 1 && p.Value.StartsWith("DXFER_NATIVE_", StringComparison.Ordinal))) return imported;
        try
        {
            var records = Records(objects).ToArray();
            var dictionaries = records.Where(r => r[0].Value == "DICTIONARY"
                && r.Any(p => p.Code == 3 && p.Value == DictionaryKey)).ToArray();
            Check(dictionaries.Length == 1, "Missing or duplicate native dictionary.");
            var dictionary = dictionaries[0];
            var keys = dictionary.Select((pair, index) => (pair, index))
                .Where(p => p.pair.Code == 3 && p.pair.Value == DictionaryKey).ToArray();
            Check(keys.Length == 1 && keys[0].index + 1 < dictionary.Length, "Invalid native dictionary entry.");
            var pointer = dictionary[keys[0].index + 1];
            Check(pointer.Code == 350, "Invalid native record ownership.");
            var matching = records.Where(r => r[0].Value == "XRECORD"
                && r.Any(p => p.Code == 5 && p.Value == pointer.Value)).ToArray();
            Check(matching.Length == 1, "Missing or duplicate native record.");
            var record = matching[0];
            Check(Single(record, 330) == Single(dictionary, 5)
                && Single(record, 100) == "AcDbXrecord", "Invalid native record owner or class.");
            var headers = record.Where(p => p.Code == 1).Select(p => p.Value).ToArray();
            Check(headers.Length == 3 && headers[0] == Version, "Unsupported native record version.");
            Check(headers[1] == Fingerprint(pairs), "DXF content changed outside DXFER; embedded intent is stale.");
            Check(!malformedRecords, "Malformed DXF records require review.");
            using var buffer = new MemoryStream();
            foreach (var chunk in record.Where(p => p.Code == 310))
            {
                Check(chunk.Value.Length is > 0 and <= 254 && chunk.Value.Length % 2 == 0,
                    "Invalid native binary chunk.");
                Check(buffer.Length + chunk.Value.Length / 2 <= DxferDocumentSerializer.MaxTextLength,
                    "Native document exceeds the embedded size limit.");
                buffer.Write(Convert.FromHexString(chunk.Value));
            }
            var bytes = buffer.ToArray();
            Check(headers[2] == Hash(bytes), "Native document checksum mismatch.");
            var native = DxferDocumentSerializer.Read(Utf8.GetString(bytes));
            restored = true;
            return native;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            return new DrawingDocument(imported.Entities, imported.Dimensions, imported.Constraints,
                imported.Metadata with
                {
                    Warnings = imported.Metadata.Warnings.Append(new DrawingDocumentWarning(IgnoredCode,
                        DrawingDocumentWarningSeverity.Warning,
                        "DXFER constraints were not restored. " + ex.Message + " Current DXF geometry was imported.")).ToArray()
                });
        }
    }

    private static IEnumerable<DxfPair[]> Records(IReadOnlyList<DxfPair> pairs)
    {
        for (var start = 0; start < pairs.Count;)
        {
            var end = start + 1;
            while (end < pairs.Count && pairs[end].Code != 0) end++;
            yield return pairs.Skip(start).Take(end - start).ToArray();
            start = end;
        }
    }

    private static IEnumerable<DxfPair> ObjectPairs(IReadOnlyList<DxfPair> pairs)
    {
        var inObjects = false;
        for (var i = 0; i < pairs.Count; i++)
        {
            if (pairs[i].Code == 0 && pairs[i].Value == "SECTION")
            {
                inObjects = i + 1 < pairs.Count && pairs[i + 1].Code == 2 && pairs[i + 1].Value == "OBJECTS";
                if (inObjects) i++;
            }
            else if (pairs[i].Code == 0 && pairs[i].Value == "ENDSEC") inObjects = false;
            else if (inObjects) yield return pairs[i];
        }
    }

    private static string Fingerprint(IReadOnlyList<DxfPair> pairs)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var inObjects = false;
        for (var i = 0; i < pairs.Count; i++)
        {
            var pair = pairs[i];
            if (pair.Code == 0 && pair.Value == "SECTION")
                inObjects = i + 1 < pairs.Count && pairs[i + 1].Code == 2 && pairs[i + 1].Value == "OBJECTS";
            if (!inObjects)
                hash.AppendData(Utf8.GetBytes(pair.Code.ToString(CultureInfo.InvariantCulture) + "\n" + pair.Value + "\n"));
            if (pair.Code == 0 && pair.Value == "ENDSEC") inObjects = false;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string Single(IEnumerable<DxfPair> pairs, int code) => pairs.Single(p => p.Code == code).Value;
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Pair(StringBuilder builder, int code, string value) =>
        builder.Append(code.ToString(CultureInfo.InvariantCulture)).Append('\n').Append(value).Append('\n');
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new FormatException(message);
    }
}
