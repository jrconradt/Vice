using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Vice.Logging;

namespace Vice.Net.Requests.Grpc;

internal static class ProtobufJsonTranscoder
{
    public const int MAX_JSON_BYTES = 16 * 1024 * 1024;
    public const int MAX_NESTING_DEPTH = 64;

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        MaxDepth = MAX_NESTING_DEPTH,
    };

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
            var raw = v.GetString() ?? string.Empty;
            if (!long.TryParse(raw,
                               NumberStyles.Integer,
                               CultureInfo.InvariantCulture,
                               out var parsed))
            {
                throw new BadArgument($"Value '{raw}' is not a valid 64-bit signed integer.");
            }

            return parsed;
        }

        return v.GetInt64();
    }

    private static ulong ReadUInt64(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.String)
        {
            var raw = v.GetString() ?? string.Empty;
            if (!ulong.TryParse(raw,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out var parsed))
            {
                throw new BadArgument($"Value '{raw}' is not a valid 64-bit unsigned integer.");
            }

            return parsed;
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

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(payload, ParseOptions);
        }
        catch (JsonException ex) when (ex.Message.Contains("maximum configured depth", StringComparison.Ordinal))
        {
            throw new BadArgument(
                $"JSON nesting exceeds transcoder limit of {MAX_NESTING_DEPTH} levels.",
                ex);
        }

        using (doc)
        {
            return EncodeMessage(doc.RootElement, descriptor, depth: 0);
        }
    }

    public static string ProtobufToJson(byte[] bytes, MessageDescriptor descriptor)
    {
        return DecodeMessage(new CodedInputStream(bytes), descriptor, depth: 0);
    }

    private sealed class EncodeOp
    {
        public bool IsChild;
        public Action<CodedOutputStream>? Inline;
        public bool ChildIsMapEntry;
        public JsonElement ChildObj;
        public string ChildMapKey = string.Empty;
        public MessageDescriptor ChildDescriptor = default!;
        public FieldDescriptor ChildMapField = default!;
        public int ChildDepth;
        public int TagFieldNumber;
    }

    private sealed class EncodeFrame
    {
        public MemoryStream Ms = default!;
        public CodedOutputStream Cos = default!;
        public List<EncodeOp> Ops = default!;
        public int Cursor;
    }

    private static byte[] EncodeMessage(JsonElement obj, MessageDescriptor descriptor, int depth)
    {
        var stack = new Stack<EncodeFrame>();
        stack.Push(NewMessageEncodeFrame(obj, descriptor, depth));
        var deliveredChild = (byte[]?)null;
        var rootResult = Array.Empty<byte>();
        while (stack.Count > 0)
        {
            var frame = stack.Peek();
            if (deliveredChild is not null)
            {
                var op = frame.Ops[frame.Cursor];
                frame.Cos.WriteTag(op.TagFieldNumber, WireFormat.WireType.LengthDelimited);
                frame.Cos.WriteBytes(ByteString.CopyFrom(deliveredChild));
                frame.Cursor++;
                deliveredChild = null;
            }

            var child = AdvanceEncodeFrame(frame);
            if (child is not null)
            {
                stack.Push(child);
                continue;
            }

            frame.Cos.Flush();
            var assembled = frame.Ms.ToArray();
            frame.Ms.Dispose();
            stack.Pop();
            if (stack.Count == 0)
            {
                rootResult = assembled;
            }
            else
            {
                deliveredChild = assembled;
            }
        }

        return rootResult;
    }

    private static EncodeFrame? AdvanceEncodeFrame(EncodeFrame frame)
    {
        while (frame.Cursor < frame.Ops.Count)
        {
            var op = frame.Ops[frame.Cursor];
            if (op.IsChild)
            {
                return op.ChildIsMapEntry
                    ? NewMapEntryEncodeFrame(op.ChildMapField, op.ChildMapKey, op.ChildObj, op.ChildDepth)
                    : NewMessageEncodeFrame(op.ChildObj, op.ChildDescriptor, op.ChildDepth);
            }

            op.Inline!(frame.Cos);
            frame.Cursor++;
        }

        return null;
    }

    private static EncodeFrame NewMessageEncodeFrame(JsonElement obj, MessageDescriptor descriptor, int depth)
    {
        if (depth > MAX_NESTING_DEPTH)
        {
            throw new BadArgument(
                $"JSON nesting exceeds transcoder limit of {MAX_NESTING_DEPTH} levels.");
        }

        var ms = new MemoryStream(EstimateEncodedSize(obj));
        var ops = new List<EncodeOp>();
        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                var field = FindField(descriptor, prop.Name);
                if (field is null)
                {
                    continue;
                }

                AppendFieldOps(ops, field, prop.Value, depth);
            }
        }

        return new EncodeFrame
        {
            Ms = ms,
            Cos = new CodedOutputStream(ms),
            Ops = ops,
        };
    }

    private static EncodeFrame NewMapEntryEncodeFrame(FieldDescriptor field,
                                                      string key,
                                                      JsonElement value,
                                                      int depth)
    {
        var keyField = field.MessageType.FindFieldByNumber(1);
        var valueField = field.MessageType.FindFieldByNumber(2);
        var ms = new MemoryStream(64);
        var ops = new List<EncodeOp>
        {
            new EncodeOp { Inline = cos => EncodeMapKey(cos, keyField, key) },
        };
        AppendSingleOp(ops, valueField, value, depth);
        return new EncodeFrame
        {
            Ms = ms,
            Cos = new CodedOutputStream(ms),
            Ops = ops,
        };
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

    private static void AppendFieldOps(List<EncodeOp> ops, FieldDescriptor field, JsonElement value, int depth)
    {
        if (field.IsMap)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var prop in value.EnumerateObject())
            {
                AppendMapEntryOp(ops, field, prop.Name, prop.Value, depth);
            }

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
                AppendSingleOp(ops, field, el, depth);
            }

            return;
        }

        AppendSingleOp(ops, field, value, depth);
    }

    private static void AppendMapEntryOp(List<EncodeOp> ops,
                                         FieldDescriptor field,
                                         string key,
                                         JsonElement value,
                                         int depth)
    {
        var valueField = field.MessageType.FindFieldByNumber(2);
        if (valueField.FieldType == FieldType.Message
            && value.ValueKind != JsonValueKind.Null)
        {
            ops.Add(new EncodeOp
            {
                IsChild = true,
                ChildIsMapEntry = true,
                ChildMapField = field,
                ChildMapKey = key,
                ChildObj = value,
                ChildDepth = depth + 1,
                TagFieldNumber = field.FieldNumber,
            });
            return;
        }

        var keyField = field.MessageType.FindFieldByNumber(1);
        var capturedValue = value;
        ops.Add(new EncodeOp
        {
            Inline = cos =>
            {
                using var entryMs = new MemoryStream(64);
                var entryCos = new CodedOutputStream(entryMs);
                EncodeMapKey(entryCos, keyField, key);
                if (capturedValue.ValueKind != JsonValueKind.Null)
                {
                    EncodeScalarSingle(entryCos, valueField, capturedValue);
                }

                entryCos.Flush();
                cos.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                cos.WriteBytes(ByteString.CopyFrom(entryMs.ToArray()));
            },
        });
    }

    private static void AppendSingleOp(List<EncodeOp> ops, FieldDescriptor field, JsonElement value, int depth)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (field.FieldType == FieldType.Message)
        {
            ops.Add(new EncodeOp
            {
                IsChild = true,
                ChildIsMapEntry = false,
                ChildObj = value,
                ChildDescriptor = field.MessageType,
                ChildDepth = depth + 1,
                TagFieldNumber = field.FieldNumber,
            });
            return;
        }

        var capturedValue = value;
        ops.Add(new EncodeOp
        {
            Inline = cos => EncodeScalarSingle(cos, field, capturedValue),
        });
    }

    private static readonly FrozenDictionary<FieldType, Action<CodedOutputStream, string>> MapKeyWriters =
        new Dictionary<FieldType, Action<CodedOutputStream, string>>
        {
            [FieldType.String] = (cos, key) => cos.WriteString(key),
            [FieldType.Bool] = (cos, key) => cos.WriteBool(ParseBool(key)),
            [FieldType.Int32] = (cos, key) => cos.WriteInt32(ParseInt32(key)),
            [FieldType.SInt32] = (cos, key) => cos.WriteSInt32(ParseInt32(key)),
            [FieldType.SFixed32] = (cos, key) => cos.WriteSFixed32(ParseInt32(key)),
            [FieldType.UInt32] = (cos, key) => cos.WriteUInt32(ParseUInt32(key)),
            [FieldType.Fixed32] = (cos, key) => cos.WriteFixed32(ParseUInt32(key)),
            [FieldType.Int64] = (cos, key) => cos.WriteInt64(ParseInt64(key)),
            [FieldType.SInt64] = (cos, key) => cos.WriteSInt64(ParseInt64(key)),
            [FieldType.SFixed64] = (cos, key) => cos.WriteSFixed64(ParseInt64(key)),
            [FieldType.UInt64] = (cos, key) => cos.WriteUInt64(ParseUInt64(key)),
            [FieldType.Fixed64] = (cos, key) => cos.WriteFixed64(ParseUInt64(key)),
        }.ToFrozenDictionary();

    private static bool ParseBool(string key)
    {
        if (!bool.TryParse(key, out var parsed))
        {
            throw new BadArgument($"Map key '{key}' is not a valid boolean.");
        }

        return parsed;
    }

    private static int ParseInt32(string key)
    {
        if (!int.TryParse(key,
                          NumberStyles.Integer,
                          CultureInfo.InvariantCulture,
                          out var parsed))
        {
            throw new BadArgument($"Map key '{key}' is not a valid 32-bit signed integer.");
        }

        return parsed;
    }

    private static uint ParseUInt32(string key)
    {
        if (!uint.TryParse(key,
                           NumberStyles.Integer,
                           CultureInfo.InvariantCulture,
                           out var parsed))
        {
            throw new BadArgument($"Map key '{key}' is not a valid 32-bit unsigned integer.");
        }

        return parsed;
    }

    private static long ParseInt64(string key)
    {
        if (!long.TryParse(key,
                           NumberStyles.Integer,
                           CultureInfo.InvariantCulture,
                           out var parsed))
        {
            throw new BadArgument($"Map key '{key}' is not a valid 64-bit signed integer.");
        }

        return parsed;
    }

    private static ulong ParseUInt64(string key)
    {
        if (!ulong.TryParse(key,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var parsed))
        {
            throw new BadArgument($"Map key '{key}' is not a valid 64-bit unsigned integer.");
        }

        return parsed;
    }

    private static void EncodeMapKey(CodedOutputStream cos, FieldDescriptor keyField, string key)
    {
        if (!MapKeyWriters.TryGetValue(keyField.FieldType, out var writer))
        {
            throw new NotSupportedException(
                $"Map key type {keyField.FieldType} (field '{keyField.FullName}') not supported by the JSON transcoder.");
        }

        cos.WriteTag(keyField.FieldNumber, WireTypeFor(keyField.FieldType));
        writer(cos, key);
    }

    private static void EncodeScalarSingle(CodedOutputStream cos, FieldDescriptor field, JsonElement value)
    {
        cos.WriteTag(field.FieldNumber, WireTypeFor(field.FieldType));
        if (Encoders.TryGetValue(field.FieldType, out var enc))
        {
            enc(cos, value);
            return;
        }

        if (field.FieldType == FieldType.Enum)
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                cos.WriteEnum(value.GetInt32());
                return;
            }

            var name = value.GetString();
            var ev = name is null ? null : field.EnumType.FindValueByName(name);
            if (ev is null)
            {
                throw new BadArgument(
                    $"Enum value '{name}' is not a declared member of {field.EnumType.FullName} (field '{field.FullName}').");
            }

            cos.WriteEnum(ev.Number);
            return;
        }

        throw new NotSupportedException(
            $"Field type {field.FieldType} (field '{field.FullName}') not supported by the JSON transcoder.");
    }

    private static WireFormat.WireType WireTypeFor(FieldType type) => type switch
    {
        FieldType.String or FieldType.Bytes or FieldType.Message => WireFormat.WireType.LengthDelimited,
        FieldType.Fixed64 or FieldType.SFixed64 or FieldType.Double => WireFormat.WireType.Fixed64,
        FieldType.Fixed32 or FieldType.SFixed32 or FieldType.Float => WireFormat.WireType.Fixed32,
        _ => WireFormat.WireType.Varint,
    };

    private enum DecodeFrameKind
    {
        Message,
        MapEntry,
    }

    private sealed class DecodeFrame
    {
        public DecodeFrameKind Kind;
        public CodedInputStream Cis = default!;
        public int Depth;
        public MessageDescriptor Descriptor = default!;
        public Dictionary<int, (FieldDescriptor Field, List<string> Values)> Collected = default!;
        public FieldDescriptor KeyField = default!;
        public FieldDescriptor ValueField = default!;
        public string MapKey = string.Empty;
        public string MapValue = string.Empty;
        public int PendingFieldNumber;
        public bool PendingIsMapValue;
    }

    private static string DecodeMessage(CodedInputStream cis, MessageDescriptor descriptor, int depth)
    {
        var stack = new Stack<DecodeFrame>();
        stack.Push(NewMessageFrame(cis, descriptor, depth));
        var deliveredChild = (string?)null;
        var rootResult = string.Empty;
        while (stack.Count > 0)
        {
            var frame = stack.Peek();
            if (deliveredChild is not null)
            {
                ApplyChildResult(frame, deliveredChild);
                deliveredChild = null;
            }

            var child = AdvanceFrame(frame);
            if (child is not null)
            {
                stack.Push(child);
                continue;
            }

            var assembled = AssembleFrame(frame);
            stack.Pop();
            if (stack.Count == 0)
            {
                rootResult = assembled;
            }
            else
            {
                deliveredChild = assembled;
            }
        }

        return rootResult;
    }

    private static DecodeFrame NewMessageFrame(CodedInputStream cis,
                                               MessageDescriptor descriptor,
                                               int depth)
    {
        if (depth > MAX_NESTING_DEPTH)
        {
            throw new BadArgument(
                $"Protobuf nesting exceeds transcoder limit of {MAX_NESTING_DEPTH} levels.");
        }

        var fieldCount = descriptor.Fields.InFieldNumberOrder().Count;
        return new DecodeFrame
        {
            Kind = DecodeFrameKind.Message,
            Cis = cis,
            Depth = depth,
            Descriptor = descriptor,
            Collected = new Dictionary<int, (FieldDescriptor Field, List<string> Values)>(fieldCount),
            PendingFieldNumber = -1,
        };
    }

    private static DecodeFrame NewMapEntryFrame(CodedInputStream entryCis,
                                                FieldDescriptor field,
                                                int depth)
    {
        var keyField = field.MessageType.FindFieldByNumber(1);
        var valueField = field.MessageType.FindFieldByNumber(2);
        return new DecodeFrame
        {
            Kind = DecodeFrameKind.MapEntry,
            Cis = entryCis,
            Depth = depth,
            KeyField = keyField,
            ValueField = valueField,
            MapKey = DefaultMapKey(keyField),
            MapValue = DefaultMapValue(valueField),
            PendingFieldNumber = -1,
        };
    }

    private static void ApplyChildResult(DecodeFrame frame, string childJson)
    {
        if (frame.Kind == DecodeFrameKind.Message)
        {
            AddToBucket(frame, frame.PendingFieldNumber, childJson);
        }
        else if (frame.PendingIsMapValue)
        {
            frame.MapValue = childJson;
        }
        else
        {
            frame.MapKey = childJson;
        }

        frame.PendingFieldNumber = -1;
        frame.PendingIsMapValue = false;
    }

    private static void AddToBucket(DecodeFrame frame, int fieldNumber, string entry)
    {
        var field = frame.Descriptor.FindFieldByNumber(fieldNumber);
        if (!frame.Collected.TryGetValue(fieldNumber, out var bucket))
        {
            frame.Collected[fieldNumber] = bucket = (field, new List<string>());
        }

        bucket.Values.Add(entry);
    }

    private static DecodeFrame? AdvanceFrame(DecodeFrame frame)
    {
        if (frame.Kind == DecodeFrameKind.Message)
        {
            return AdvanceMessageFrame(frame);
        }

        return AdvanceMapEntryFrame(frame);
    }

    private static DecodeFrame? AdvanceMessageFrame(DecodeFrame frame)
    {
        uint tag;
        while ((tag = frame.Cis.ReadTag()) != 0)
        {
            var fieldNumber = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);
            var field = frame.Descriptor.FindFieldByNumber(fieldNumber);
            if (field is null)
            {
                SkipField(frame.Cis, wireType);
                continue;
            }

            if (field.IsMap)
            {
                var entryBytes = frame.Cis.ReadBytes().ToByteArray();
                var entryFrame = NewMapEntryFrame(new CodedInputStream(entryBytes), field, frame.Depth);
                frame.PendingFieldNumber = fieldNumber;
                frame.PendingIsMapValue = false;
                return entryFrame;
            }

            if (field.FieldType == FieldType.Message)
            {
                var nestedBytes = frame.Cis.ReadBytes().ToByteArray();
                var nestedFrame = NewMessageFrame(new CodedInputStream(nestedBytes),
                                                  field.MessageType,
                                                  frame.Depth + 1);
                frame.PendingFieldNumber = fieldNumber;
                frame.PendingIsMapValue = false;
                return nestedFrame;
            }

            if (field.IsRepeated
                && wireType == WireFormat.WireType.LengthDelimited
                && IsPackableScalar(field.FieldType))
            {
                var packedBytes = frame.Cis.ReadBytes().ToByteArray();
                var packedCis = new CodedInputStream(packedBytes);
                while (!packedCis.IsAtEnd)
                {
                    AddToBucket(frame, fieldNumber, DecodeScalarValue(packedCis, field));
                }

                continue;
            }

            AddToBucket(frame, fieldNumber, DecodeScalarValue(frame.Cis, field));
        }

        return null;
    }

    private static DecodeFrame? AdvanceMapEntryFrame(DecodeFrame frame)
    {
        uint tag;
        while ((tag = frame.Cis.ReadTag()) != 0)
        {
            var fieldNumber = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);
            if (fieldNumber == frame.ValueField.FieldNumber
                && frame.ValueField.FieldType == FieldType.Message)
            {
                var nestedBytes = frame.Cis.ReadBytes().ToByteArray();
                var nestedFrame = NewMessageFrame(new CodedInputStream(nestedBytes),
                                                  frame.ValueField.MessageType,
                                                  frame.Depth + 1);
                frame.PendingFieldNumber = fieldNumber;
                frame.PendingIsMapValue = true;
                return nestedFrame;
            }

            if (fieldNumber == frame.KeyField.FieldNumber)
            {
                frame.MapKey = DecodeScalarValue(frame.Cis, frame.KeyField);
            }
            else if (fieldNumber == frame.ValueField.FieldNumber)
            {
                frame.MapValue = DecodeScalarValue(frame.Cis, frame.ValueField);
            }
            else
            {
                SkipField(frame.Cis, wireType);
            }
        }

        return null;
    }

    private static string AssembleFrame(DecodeFrame frame)
    {
        if (frame.Kind == DecodeFrameKind.MapEntry)
        {
            return $"{MapKeyToJsonName(frame.MapKey)}:{frame.MapValue}";
        }

        var members = new List<string>(frame.Collected.Count);
        foreach (var (_, bucket) in frame.Collected)
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

    private static bool IsPackableScalar(FieldType type)
    {
        return type switch
        {
            FieldType.String or FieldType.Bytes or FieldType.Message or FieldType.Group => false,
            _ => true,
        };
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

    private static string DecodeScalarValue(CodedInputStream cis, FieldDescriptor field)
    {
        if (Decoders.TryGetValue(field.FieldType, out var dec))
        {
            return dec(cis);
        }

        if (field.FieldType == FieldType.Enum)
        {
            var num = cis.ReadEnum();
            var ev = field.EnumType.FindValueByNumber(num);
            return ev is null
                ? num.ToString(CultureInfo.InvariantCulture)
                : $"\"{JsonEscape(ev.Name)}\"";
        }

        throw new NotSupportedException(
            $"Field type {field.FieldType} (field '{field.FullName}') not supported by the JSON transcoder.");
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
