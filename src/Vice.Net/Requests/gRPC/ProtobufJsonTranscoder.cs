using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Vice.Logging;

namespace Vice.Network.gRPC;

internal static class ProtobufJsonTranscoder
{
    public const int MAX_JSON_BYTES = 16 * 1024 * 1024;
    public const int MAX_NESTING_DEPTH = 64;

    private static readonly FrozenDictionary<FieldType, Action<CodedOutputStream, JsonElement>> Encoders =
        new Dictionary<FieldType, Action<CodedOutputStream, JsonElement>>
        {
            [FieldType.String] = (cos, v) => cos.WriteString(v.GetString() ?? string.Empty),
            [FieldType.Bool] = (cos, v) => cos.WriteBool(v.GetBoolean()),
            [FieldType.Int32] = (cos, v) => cos.WriteInt32(v.GetInt32()),
            [FieldType.SInt32] = (cos, v) => cos.WriteSInt32(v.GetInt32()),
            [FieldType.SFixed32] = (cos, v) => cos.WriteSFixed32(v.GetInt32()),
            [FieldType.UInt32] = (cos, v) => cos.WriteUInt32(v.GetUInt32()),
            [FieldType.Fixed32] = (cos, v) => cos.WriteFixed32(v.GetUInt32()),
            [FieldType.Int64] = (cos, v) => cos.WriteInt64(ReadInt64(v)),
            [FieldType.SInt64] = (cos, v) => cos.WriteSInt64(ReadInt64(v)),
            [FieldType.SFixed64] = (cos, v) => cos.WriteSFixed64(ReadInt64(v)),
            [FieldType.UInt64] = (cos, v) => cos.WriteUInt64(ReadUInt64(v)),
            [FieldType.Fixed64] = (cos, v) => cos.WriteFixed64(ReadUInt64(v)),
            [FieldType.Float] = (cos, v) => cos.WriteFloat(v.GetSingle()),
            [FieldType.Double] = (cos, v) => cos.WriteDouble(v.GetDouble()),
            [FieldType.Bytes] = (cos, v) => cos.WriteBytes(ByteString.FromBase64(v.GetString() ?? string.Empty)),
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<FieldType, Func<CodedInputStream, string>> Decoders =
        new Dictionary<FieldType, Func<CodedInputStream, string>>
        {
            [FieldType.String] = cis => $"\"{JsonEscape(cis.ReadString())}\"",
            [FieldType.Bool] = cis => cis.ReadBool() ? "true" : "false",
            [FieldType.Int32] = cis => cis.ReadInt32().ToString(CultureInfo.InvariantCulture),
            [FieldType.SInt32] = cis => cis.ReadSInt32().ToString(CultureInfo.InvariantCulture),
            [FieldType.SFixed32] = cis => cis.ReadSFixed32().ToString(CultureInfo.InvariantCulture),
            [FieldType.UInt32] = cis => cis.ReadUInt32().ToString(CultureInfo.InvariantCulture),
            [FieldType.Fixed32] = cis => cis.ReadFixed32().ToString(CultureInfo.InvariantCulture),
            [FieldType.Int64] = cis => $"\"{cis.ReadInt64().ToString(CultureInfo.InvariantCulture)}\"",
            [FieldType.SInt64] = cis => $"\"{cis.ReadSInt64().ToString(CultureInfo.InvariantCulture)}\"",
            [FieldType.SFixed64] = cis => $"\"{cis.ReadSFixed64().ToString(CultureInfo.InvariantCulture)}\"",
            [FieldType.UInt64] = cis => $"\"{cis.ReadUInt64().ToString(CultureInfo.InvariantCulture)}\"",
            [FieldType.Fixed64] = cis => $"\"{cis.ReadFixed64().ToString(CultureInfo.InvariantCulture)}\"",
            [FieldType.Float] = cis => cis.ReadFloat().ToString("R", CultureInfo.InvariantCulture),
            [FieldType.Double] = cis => cis.ReadDouble().ToString("R", CultureInfo.InvariantCulture),
            [FieldType.Bytes] = cis => $"\"{Convert.ToBase64String(cis.ReadBytes().ToByteArray())}\"",
        }.ToFrozenDictionary();

    private static long ReadInt64(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.String)
        {
            return long.Parse(v.GetString() ?? string.Empty, CultureInfo.InvariantCulture);
        }

        return v.GetInt64();
    }

    private static ulong ReadUInt64(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.String)
        {
            return ulong.Parse(v.GetString() ?? string.Empty, CultureInfo.InvariantCulture);
        }

        return v.GetUInt64();
    }

    public static byte[] JsonToProtobuf(string json, MessageDescriptor descriptor)
    {
        var payload = string.IsNullOrWhiteSpace(json) ? "{}" : json;
        if (Encoding.UTF8.GetByteCount(payload) > MAX_JSON_BYTES)
        {
            throw new BadArgument(
                $"JSON payload exceeds transcoder limit of {MAX_JSON_BYTES} bytes.");
        }

        using var doc = JsonDocument.Parse(payload);
        return EncodeMessage(doc.RootElement, descriptor, depth: 0);
    }

    public static string ProtobufToJson(byte[] bytes, MessageDescriptor descriptor)
    {
        return DecodeMessage(new CodedInputStream(bytes), descriptor, depth: 0);
    }

    private static byte[] EncodeMessage(JsonElement obj, MessageDescriptor descriptor, int depth)
    {
        if (depth > MAX_NESTING_DEPTH)
        {
            throw new BadArgument(
                $"JSON nesting exceeds transcoder limit of {MAX_NESTING_DEPTH} levels.");
        }

        using var ms = new MemoryStream(EstimateEncodedSize(obj));
        var cos = new CodedOutputStream(ms);
        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                var field = FindField(descriptor, prop.Name);
                if (field is null)
                {
                    continue;
                }

                EncodeField(cos, field, prop.Value, depth);
            }
        }
        cos.Flush();
        return ms.ToArray();
    }

    private static int EstimateEncodedSize(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object && obj.ValueKind != JsonValueKind.Array)
        {
            return 64;
        }

        var raw = obj.GetRawText().Length;
        return raw < 64 ? 64 : raw;
    }

    private static FieldDescriptor? FindField(MessageDescriptor descriptor, string name)
    {
        return descriptor.FindFieldByName(name)
               ?? descriptor.Fields.InDeclarationOrder()
                    .FirstOrDefault(f => f.JsonName == name);
    }

    private static void EncodeField(CodedOutputStream cos, FieldDescriptor field, JsonElement value, int depth)
    {
        if (field.IsMap)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            EncodeMap(cos, field, value, depth);
            return;
        }
        if (field.IsRepeated)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var el in value.EnumerateArray())
            {
                EncodeSingle(cos, field, el, depth);
            }

            return;
        }
        EncodeSingle(cos, field, value, depth);
    }

    private static void EncodeMap(CodedOutputStream cos, FieldDescriptor field, JsonElement value, int depth)
    {
        var keyField = field.MessageType.FindFieldByNumber(1);
        var valueField = field.MessageType.FindFieldByNumber(2);
        foreach (var prop in value.EnumerateObject())
        {
            using var ms = new MemoryStream(64);
            var entryCos = new CodedOutputStream(ms);
            EncodeMapKey(entryCos, keyField, prop.Name);
            EncodeSingle(entryCos, valueField, prop.Value, depth);
            entryCos.Flush();
            cos.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
            cos.WriteBytes(ByteString.CopyFrom(ms.ToArray()));
        }
    }

    private static void EncodeMapKey(CodedOutputStream cos, FieldDescriptor keyField, string key)
    {
        cos.WriteTag(keyField.FieldNumber, WireTypeFor(keyField.FieldType));
        switch (keyField.FieldType)
        {
            case FieldType.String:
                cos.WriteString(key);
                break;
            case FieldType.Bool:
                cos.WriteBool(bool.Parse(key));
                break;
            case FieldType.Int32:
                cos.WriteInt32(int.Parse(key, CultureInfo.InvariantCulture));
                break;
            case FieldType.SInt32:
                cos.WriteSInt32(int.Parse(key, CultureInfo.InvariantCulture));
                break;
            case FieldType.SFixed32:
                cos.WriteSFixed32(int.Parse(key, CultureInfo.InvariantCulture));
                break;
            case FieldType.UInt32:
                cos.WriteUInt32(uint.Parse(key, CultureInfo.InvariantCulture));
                break;
            case FieldType.Fixed32:
                cos.WriteFixed32(uint.Parse(key, CultureInfo.InvariantCulture));
                break;
            case FieldType.Int64:
                cos.WriteInt64(long.Parse(key, CultureInfo.InvariantCulture));
                break;
            case FieldType.SInt64:
                cos.WriteSInt64(long.Parse(key, CultureInfo.InvariantCulture));
                break;
            case FieldType.SFixed64:
                cos.WriteSFixed64(long.Parse(key, CultureInfo.InvariantCulture));
                break;
            case FieldType.UInt64:
                cos.WriteUInt64(ulong.Parse(key, CultureInfo.InvariantCulture));
                break;
            case FieldType.Fixed64:
                cos.WriteFixed64(ulong.Parse(key, CultureInfo.InvariantCulture));
                break;
            default:
                throw new NotSupportedException(
                    $"Map key type {keyField.FieldType} (field '{keyField.FullName}') not supported by the JSON transcoder.");
        }
    }

    private static void EncodeSingle(CodedOutputStream cos, FieldDescriptor field, JsonElement value, int depth)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        cos.WriteTag(field.FieldNumber, WireTypeFor(field.FieldType));
        if (Encoders.TryGetValue(field.FieldType, out var enc))
        {
            enc(cos, value);
            return;
        }
        switch (field.FieldType)
        {
            case FieldType.Enum:
                if (value.ValueKind == JsonValueKind.Number)
                {
                    cos.WriteEnum(value.GetInt32());
                }
                else
                {
                    var name = value.GetString();
                    var ev = name is null ? null : field.EnumType.FindValueByName(name);
                    cos.WriteEnum(ev?.Number ?? 0);
                }
                return;
            case FieldType.Message:
                var nested = EncodeMessage(value, field.MessageType, depth + 1);
                cos.WriteBytes(ByteString.CopyFrom(nested));
                return;
            default:
                throw new NotSupportedException(
                    $"Field type {field.FieldType} (field '{field.FullName}') not supported by the JSON transcoder.");
        }
    }

    private static WireFormat.WireType WireTypeFor(FieldType type) => type switch
    {
        FieldType.String or FieldType.Bytes or FieldType.Message => WireFormat.WireType.LengthDelimited,
        FieldType.Fixed64 or FieldType.SFixed64 or FieldType.Double => WireFormat.WireType.Fixed64,
        FieldType.Fixed32 or FieldType.SFixed32 or FieldType.Float => WireFormat.WireType.Fixed32,
        _ => WireFormat.WireType.Varint,
    };

    private static string DecodeMessage(CodedInputStream cis, MessageDescriptor descriptor, int depth)
    {
        if (depth > MAX_NESTING_DEPTH)
        {
            throw new BadArgument(
                $"Protobuf nesting exceeds transcoder limit of {MAX_NESTING_DEPTH} levels.");
        }

        var fieldCount = descriptor.Fields.InFieldNumberOrder().Count;
        var collected = new Dictionary<int, (FieldDescriptor Field, List<string> Values)>(fieldCount);
        uint tag;
        while ((tag = cis.ReadTag()) != 0)
        {
            var fieldNumber = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);
            var field = descriptor.FindFieldByNumber(fieldNumber);
            if (field is null)
            {
                SkipField(cis, wireType);
                continue;
            }
            var entry = field.IsMap
                ? DecodeMapEntry(cis, field, depth)
                : DecodeFieldValue(cis, field, depth);
            if (!collected.TryGetValue(fieldNumber, out var bucket))
            {
                collected[fieldNumber] = bucket = (field, new List<string>());
            }
            bucket.Values.Add(entry);
        }

        var members = new List<string>(collected.Count);
        foreach (var (_, bucket) in collected)
        {
            var name = $"\"{JsonEscape(bucket.Field.JsonName)}\":";
            if (bucket.Field.IsMap)
            {
                members.Add($"{name}{{{string.Join(",", bucket.Values)}}}");
            }
            else if (bucket.Field.IsRepeated)
            {
                members.Add($"{name}[{string.Join(",", bucket.Values)}]");
            }
            else
            {
                members.Add($"{name}{bucket.Values[^1]}");
            }
        }
        return $"{{{string.Join(",", members)}}}";
    }

    private static string DecodeMapEntry(CodedInputStream cis, FieldDescriptor field, int depth)
    {
        var keyField = field.MessageType.FindFieldByNumber(1);
        var valueField = field.MessageType.FindFieldByNumber(2);
        var entryBytes = cis.ReadBytes().ToByteArray();
        var entryCis = new CodedInputStream(entryBytes);
        var key = DefaultMapKey(keyField);
        var valueJson = DefaultMapValue(valueField);
        uint tag;
        while ((tag = entryCis.ReadTag()) != 0)
        {
            var fieldNumber = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);
            if (fieldNumber == keyField.FieldNumber)
            {
                key = DecodeFieldValue(entryCis, keyField, depth);
            }
            else if (fieldNumber == valueField.FieldNumber)
            {
                valueJson = DecodeFieldValue(entryCis, valueField, depth);
            }
            else
            {
                SkipField(entryCis, wireType);
            }
        }
        return $"{MapKeyToJsonName(key)}:{valueJson}";
    }

    private static string DefaultMapKey(FieldDescriptor keyField)
    {
        return keyField.FieldType == FieldType.Bool ? "false" : "0";
    }

    private static string DefaultMapValue(FieldDescriptor valueField)
    {
        return valueField.FieldType switch
        {
            FieldType.String or FieldType.Bytes => "\"\"",
            FieldType.Bool => "false",
            FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64
                or FieldType.UInt64 or FieldType.Fixed64 => "\"0\"",
            FieldType.Message => "{}",
            _ => "0",
        };
    }

    private static string MapKeyToJsonName(string decodedKey)
    {
        if (decodedKey.Length > 0 && decodedKey[0] == '"')
        {
            return decodedKey;
        }
        return $"\"{decodedKey}\"";
    }

    private static string DecodeFieldValue(CodedInputStream cis, FieldDescriptor field, int depth)
    {
        if (Decoders.TryGetValue(field.FieldType, out var dec))
        {
            return dec(cis);
        }

        switch (field.FieldType)
        {
            case FieldType.Enum:
                var num = cis.ReadEnum();
                var ev = field.EnumType.FindValueByNumber(num);
                return ev is null
                    ? num.ToString(CultureInfo.InvariantCulture)
                    : $"\"{JsonEscape(ev.Name)}\"";
            case FieldType.Message:
                var nestedBytes = cis.ReadBytes().ToByteArray();
                var nested = DecodeMessage(new CodedInputStream(nestedBytes), field.MessageType, depth + 1);
                return nested;
            default:
                throw new NotSupportedException(
                    $"Field type {field.FieldType} (field '{field.FullName}') not supported by the JSON transcoder.");
        }
    }

    private static void SkipField(CodedInputStream cis, WireFormat.WireType wireType)
    {
        switch (wireType)
        {
            case WireFormat.WireType.Varint:
                cis.ReadInt64();
                break;
            case WireFormat.WireType.Fixed64:
                cis.ReadFixed64();
                break;
            case WireFormat.WireType.LengthDelimited:
                _ = cis.ReadBytes();
                break;
            case WireFormat.WireType.Fixed32:
                cis.ReadFixed32();
                break;
            default:
                throw new InvalidOperationException($"Cannot skip wire type {wireType}");
        }
    }

    private static bool NeedsEscaping(string s)
    {
        foreach (var c in s)
        {
            if (c == '"'
                || c == '\\'
                || c < 0x20)
            {
                return true;
            }
        }
        return false;
    }

    private static string JsonEscape(string s)
    {
        if (!NeedsEscaping(s))
        {
            return s;
        }

        var buffer = new List<char>(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"':
                    buffer.Add('\\');
                    buffer.Add('"');
                    break;
                case '\\':
                    buffer.Add('\\');
                    buffer.Add('\\');
                    break;
                case '\b':
                    buffer.Add('\\');
                    buffer.Add('b');
                    break;
                case '\f':
                    buffer.Add('\\');
                    buffer.Add('f');
                    break;
                case '\n':
                    buffer.Add('\\');
                    buffer.Add('n');
                    break;
                case '\r':
                    buffer.Add('\\');
                    buffer.Add('r');
                    break;
                case '\t':
                    buffer.Add('\\');
                    buffer.Add('t');
                    break;
                default:
                    if (c < 0x20)
                    {
                        buffer.AddRange($"\\u{(int)c:X4}");
                    }
                    else
                    {
                        buffer.Add(c);
                    }
                    break;
            }
        }
        return new string(buffer.ToArray());
    }
}
