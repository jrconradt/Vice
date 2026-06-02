using System.Globalization;
using System.Text.Json;
using CsCheck;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Vice.Logging;
using Vice.Net.Requests.Grpc;
using Xunit;

namespace Vice.Net.Tests;

public class ProtobufJsonTranscoderPropertyTests
{
    private const long Iterations = 2_000;

    private static MessageDescriptor KitchenMessage()
        => BuildKitchenDescriptor().FindTypeByName<MessageDescriptor>("Kitchen");

    private static FileDescriptor BuildKitchenDescriptor()
    {
        var fileProto = new FileDescriptorProto
        {
            Name = "kitchen_property.proto",
            Package = "vice.proptest",
            Syntax = "proto3",
        };

        var color = new EnumDescriptorProto { Name = "Color" };
        color.Value.Add(new EnumValueDescriptorProto { Name = "RED", Number = 0 });
        color.Value.Add(new EnumValueDescriptorProto { Name = "GREEN", Number = 1 });
        color.Value.Add(new EnumValueDescriptorProto { Name = "BLUE", Number = 2 });
        fileProto.EnumType.Add(color);

        var inner = new DescriptorProto { Name = "Inner" };
        inner.Field.Add(ScalarField("label", 1, FieldDescriptorProto.Types.Type.String));
        inner.Field.Add(ScalarField("weight", 2, FieldDescriptorProto.Types.Type.Int64));
        fileProto.MessageType.Add(inner);

        var msg = new DescriptorProto { Name = "Kitchen" };
        msg.Field.Add(ScalarField("name", 1, FieldDescriptorProto.Types.Type.String));
        msg.Field.Add(ScalarField("count", 2, FieldDescriptorProto.Types.Type.Int32));
        msg.Field.Add(ScalarField("active", 3, FieldDescriptorProto.Types.Type.Bool));
        msg.Field.Add(EnumField("shade", 4, ".vice.proptest.Color"));
        msg.Field.Add(RepeatedField("items", 5, FieldDescriptorProto.Types.Type.Int32));
        msg.Field.Add(MessageField("inner", 6, ".vice.proptest.Inner"));
        msg.Field.Add(ScalarField("big", 7, FieldDescriptorProto.Types.Type.Int64));
        msg.Field.Add(RepeatedField("tags", 8, FieldDescriptorProto.Types.Type.String));
        fileProto.MessageType.Add(msg);

        var files = FileDescriptor.BuildFromByteStrings(new[] { fileProto.ToByteString() });
        return files[0];
    }

    private static FieldDescriptorProto ScalarField(string name, int number, FieldDescriptorProto.Types.Type type)
    {
        return new FieldDescriptorProto
        {
            Name = name,
            Number = number,
            Type = type,
            Label = FieldDescriptorProto.Types.Label.Optional,
        };
    }

    private static FieldDescriptorProto RepeatedField(string name, int number, FieldDescriptorProto.Types.Type type)
    {
        return new FieldDescriptorProto
        {
            Name = name,
            Number = number,
            Type = type,
            Label = FieldDescriptorProto.Types.Label.Repeated,
        };
    }

    private static FieldDescriptorProto EnumField(string name, int number, string typeName)
    {
        return new FieldDescriptorProto
        {
            Name = name,
            Number = number,
            Type = FieldDescriptorProto.Types.Type.Enum,
            TypeName = typeName,
            Label = FieldDescriptorProto.Types.Label.Optional,
        };
    }

    private static FieldDescriptorProto MessageField(string name, int number, string typeName)
    {
        return new FieldDescriptorProto
        {
            Name = name,
            Number = number,
            Type = FieldDescriptorProto.Types.Type.Message,
            TypeName = typeName,
            Label = FieldDescriptorProto.Types.Label.Optional,
        };
    }

    private static readonly Gen<string> Text =
        Gen.OneOfConst(
            "ascii",
            "with \"quotes\" and \\backslash\\",
            "tab\tnewline\nreturn\r",
            "emoji 🚀 and accents éüñ",
            "control  end",
            "");

    private static readonly Gen<int> Int32Value =
        Gen.OneOf(
            Gen.Int,
            Gen.OneOfConst(int.MinValue, int.MinValue + 1, -1, 0, 1, int.MaxValue - 1, int.MaxValue));

    private static readonly Gen<long> Int64Value =
        Gen.OneOf(
            Gen.Long,
            Gen.OneOfConst(long.MinValue, long.MinValue + 1, -1L, 0L, 1L, long.MaxValue - 1, long.MaxValue));

    private static readonly Gen<string> ShadeValue =
        Gen.OneOf(
            Gen.Int[0, 2].Select(n => n.ToString(CultureInfo.InvariantCulture)),
            Gen.OneOfConst("\"RED\"", "\"GREEN\"", "\"BLUE\""));

    private sealed record Kitchen(
        bool HasName, string Name,
        bool HasCount, int Count,
        bool HasActive, bool Active,
        bool HasShade, string Shade,
        int[]? Items,
        bool HasInnerLabel, string InnerLabel,
        bool HasInnerWeight, long InnerWeight,
        bool HasBig, long Big,
        string[]? Tags);

    private static readonly Gen<Kitchen> KitchenGen =
        from hasName in Gen.Bool
        from name in Text
        from hasCount in Gen.Bool
        from count in Int32Value
        from hasActive in Gen.Bool
        from active in Gen.Bool
        from hasShade in Gen.Bool
        from shade in ShadeValue
        from items in Gen.Null(Int32Value.Array[0, 5])
        from hasInnerLabel in Gen.Bool
        from innerLabel in Text
        from hasInnerWeight in Gen.Bool
        from innerWeight in Int64Value
        from hasBig in Gen.Bool
        from big in Int64Value
        from tags in Gen.Null(Text.Array[0, 4])
        select new Kitchen(
            hasName, name,
            hasCount, count,
            hasActive, active,
            hasShade, shade,
            items,
            hasInnerLabel, innerLabel,
            hasInnerWeight, innerWeight,
            hasBig, big,
            tags);

    private static string Render(Kitchen k)
    {
        var members = new List<string>();
        if (k.HasName)
        {
            members.Add($"\"name\":{JsonString(k.Name)}");
        }

        if (k.HasCount)
        {
            members.Add($"\"count\":{k.Count.ToString(CultureInfo.InvariantCulture)}");
        }

        if (k.HasActive)
        {
            members.Add($"\"active\":{(k.Active ? "true" : "false")}");
        }

        if (k.HasShade)
        {
            members.Add($"\"shade\":{k.Shade}");
        }

        if (k.Items is not null)
        {
            var elems = k.Items.Select(v => v.ToString(CultureInfo.InvariantCulture));
            members.Add($"\"items\":[{string.Join(",", elems)}]");
        }

        if (k.HasInnerLabel || k.HasInnerWeight)
        {
            var innerMembers = new List<string>();
            if (k.HasInnerLabel)
            {
                innerMembers.Add($"\"label\":{JsonString(k.InnerLabel)}");
            }

            if (k.HasInnerWeight)
            {
                innerMembers.Add($"\"weight\":{JsonString(k.InnerWeight.ToString(CultureInfo.InvariantCulture))}");
            }

            members.Add($"\"inner\":{{{string.Join(",", innerMembers)}}}");
        }

        if (k.HasBig)
        {
            members.Add($"\"big\":{JsonString(k.Big.ToString(CultureInfo.InvariantCulture))}");
        }

        if (k.Tags is not null)
        {
            var elems = k.Tags.Select(JsonString);
            members.Add($"\"tags\":[{string.Join(",", elems)}]");
        }

        return $"{{{string.Join(",", members)}}}";
    }

    private static string JsonString(string value)
    {
        return JsonSerializer.Serialize(value);
    }

    private static readonly Gen<string> ArbitraryText =
        Gen.Frequency(
            (3, KitchenGen.Select(Render)),
            (2, Gen.Int[1, 9].Select(extra =>
            {
                var depth = ProtobufJsonTranscoder.MAX_NESTING_DEPTH + extra;
                return string.Concat(Enumerable.Repeat("{\"inner\":", depth))
                    + "{\"label\":\"x\"}"
                    + new string('}', depth);
            })),
            (1, Gen.Const("{ this is not json")),
            (1, Gen.String[Gen.Char[(char)32, (char)126], 0, 64]),
            (1, Gen.Const("{\"count\":\"not-a-number\"}")),
            (1, Gen.Const("{\"items\":\"should-be-array\",\"inner\":42}")));

    [Fact]
    public void Json_bytes_json_bytes_is_a_fixed_point_over_generated_messages()
    {
        var desc = KitchenMessage();
        KitchenGen.Select(Render).Sample(json =>
            {
                var firstBytes = ProtobufJsonTranscoder.JsonToProtobuf(json, desc);
                var decoded = ProtobufJsonTranscoder.ProtobufToJson(firstBytes, desc);

                using (JsonDocument.Parse(decoded))
                {
                }

                var secondBytes = ProtobufJsonTranscoder.JsonToProtobuf(decoded, desc);

                Assert.True(
                    firstBytes.AsSpan().SequenceEqual(secondBytes),
                    $"Roundtrip diverged for input '{json}' -> decoded '{decoded}'");
            },
            iter: Iterations,
            seed: "0000TranscoderFixed");
    }

    [Fact]
    public void Decoded_json_preserves_scalar_and_repeated_field_values()
    {
        var desc = KitchenMessage();
        KitchenGen.Select(Render).Sample(json =>
            {
                using var input = JsonDocument.Parse(json);

                var bytes = ProtobufJsonTranscoder.JsonToProtobuf(json, desc);
                var decoded = ProtobufJsonTranscoder.ProtobufToJson(bytes, desc);
                using var output = JsonDocument.Parse(decoded);

                if (input.RootElement.TryGetProperty("count", out var count))
                {
                    var expectedCount = count.GetInt32();
                    var actualCount = output.RootElement.TryGetProperty("count", out var outCount)
                        ? outCount.GetInt32()
                        : 0;
                    Assert.Equal(expectedCount, actualCount);
                }

                if (input.RootElement.TryGetProperty("active", out var active))
                {
                    var expectedActive = active.GetBoolean();
                    var actualActive = output.RootElement.TryGetProperty("active", out var outActive)
                        && outActive.GetBoolean();
                    Assert.Equal(expectedActive, actualActive);
                }

                if (input.RootElement.TryGetProperty("items", out var items))
                {
                    var expected = items.EnumerateArray().Select(e => e.GetInt32()).ToArray();
                    var actual = output.RootElement.TryGetProperty("items", out var outItems)
                        ? outItems.EnumerateArray().Select(e => e.GetInt32()).ToArray()
                        : Array.Empty<int>();
                    Assert.Equal(expected, actual);
                }
            },
            iter: Iterations,
            seed: "0000TranscoderValue");
    }

    [Fact]
    public void Arbitrary_text_only_fails_with_guarded_exceptions()
    {
        var desc = KitchenMessage();
        ArbitraryText.Sample(payload =>
            {
                try
                {
                    var bytes = ProtobufJsonTranscoder.JsonToProtobuf(payload, desc);
                    var decoded = ProtobufJsonTranscoder.ProtobufToJson(bytes, desc);
                    using (JsonDocument.Parse(decoded))
                    {
                    }
                }
                catch (Exception ex) when (ex is JsonException
                                           or InvalidOperationException
                                           or BadArgument
                                           or FormatException
                                           or OverflowException)
                {
                }
            },
            iter: Iterations,
            seed: "0000TranscoderText0");
    }

    [Fact]
    public void Arbitrary_bytes_decode_only_fails_with_guarded_exceptions()
    {
        var desc = KitchenMessage();
        Gen.Byte.Array[0, 256].Sample(buffer =>
            {
                try
                {
                    var decoded = ProtobufJsonTranscoder.ProtobufToJson(buffer, desc);
                    using (JsonDocument.Parse(decoded))
                    {
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException
                                           or BadArgument
                                           or InvalidProtocolBufferException
                                           or FormatException
                                           or OverflowException
                                           or JsonException)
                {
                }
            },
            iter: Iterations,
            seed: "0000TranscoderBytes");
    }
}
