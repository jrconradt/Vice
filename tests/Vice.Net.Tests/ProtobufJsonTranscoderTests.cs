using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Vice.Logging;
using Vice.Net.Requests.Grpc;
using Vice.Net.Tests.Grpc;
using Xunit;

namespace Vice.Net.Tests;

public class ProtobufJsonTranscoderTests
{
    private static FileDescriptor BuildKitchenSinkDescriptor()
    {
        var fileProto = new FileDescriptorProto
        {
            Name = "kitchen.proto",
            Package = "vice.test",
            Syntax = "proto3",
        };

        var color = new EnumDescriptorProto { Name = "Color" };
        color.Value.Add(new EnumValueDescriptorProto { Name = "RED", Number = 0 });
        color.Value.Add(new EnumValueDescriptorProto { Name = "GREEN", Number = 1 });
        color.Value.Add(new EnumValueDescriptorProto { Name = "BLUE", Number = 2 });
        fileProto.EnumType.Add(color);

        var inner = new DescriptorProto { Name = "Inner" };
        inner.Field.Add(new FieldDescriptorProto
        {
            Name = "label",
            Number = 1,
            Type = FieldDescriptorProto.Types.Type.String,
            Label = FieldDescriptorProto.Types.Label.Optional,
        });
        fileProto.MessageType.Add(inner);

        var msg = new DescriptorProto { Name = "Kitchen" };
        msg.Field.Add(new FieldDescriptorProto
        {
            Name = "name",
            Number = 1,
            Type = FieldDescriptorProto.Types.Type.String,
            Label = FieldDescriptorProto.Types.Label.Optional,
        });
        msg.Field.Add(new FieldDescriptorProto
        {
            Name = "count",
            Number = 2,
            Type = FieldDescriptorProto.Types.Type.Int32,
            Label = FieldDescriptorProto.Types.Label.Optional,
        });
        msg.Field.Add(new FieldDescriptorProto
        {
            Name = "active",
            Number = 3,
            Type = FieldDescriptorProto.Types.Type.Bool,
            Label = FieldDescriptorProto.Types.Label.Optional,
        });
        msg.Field.Add(new FieldDescriptorProto
        {
            Name = "shade",
            Number = 4,
            Type = FieldDescriptorProto.Types.Type.Enum,
            TypeName = ".vice.test.Color",
            Label = FieldDescriptorProto.Types.Label.Optional,
        });
        msg.Field.Add(new FieldDescriptorProto
        {
            Name = "items",
            Number = 5,
            Type = FieldDescriptorProto.Types.Type.Int32,
            Label = FieldDescriptorProto.Types.Label.Repeated,
        });
        msg.Field.Add(new FieldDescriptorProto
        {
            Name = "inner",
            Number = 6,
            Type = FieldDescriptorProto.Types.Type.Message,
            TypeName = ".vice.test.Inner",
            Label = FieldDescriptorProto.Types.Label.Optional,
        });
        fileProto.MessageType.Add(msg);

        var bytes = fileProto.ToByteString();
        var files = FileDescriptor.BuildFromByteStrings(new[] { bytes });
        return files[0];
    }

    private static MessageDescriptor Kitchen()
        => BuildKitchenSinkDescriptor().FindTypeByName<MessageDescriptor>("Kitchen");

    [Fact]
    public void Roundtrip_string_field()
    {
        var bytes = ProtobufJsonTranscoder.JsonToProtobuf(
            "{\"name\":\"hello\"}", HelloRequest.Descriptor);
        var json = ProtobufJsonTranscoder.ProtobufToJson(bytes, HelloRequest.Descriptor);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("hello", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void Roundtrip_int_string_bool_enum_simple_scalars()
    {
        var desc = Kitchen();
        const string INPUT = "{\"name\":\"foo\",\"count\":42,\"active\":true,\"shade\":\"GREEN\"}";

        var bytes = ProtobufJsonTranscoder.JsonToProtobuf(INPUT, desc);
        var output = ProtobufJsonTranscoder.ProtobufToJson(bytes, desc);

        using var doc = JsonDocument.Parse(output);
        Assert.Equal("foo", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal(42, doc.RootElement.GetProperty("count").GetInt32());
        Assert.True(doc.RootElement.GetProperty("active").GetBoolean());
        Assert.Equal("GREEN", doc.RootElement.GetProperty("shade").GetString());
    }

    [Fact]
    public void Repeated_int32_field_encodes_and_decodes_all_elements()
    {
        var desc = Kitchen();
        const string INPUT = "{\"items\":[1,2,3]}";

        var bytes = ProtobufJsonTranscoder.JsonToProtobuf(INPUT, desc);
        var output = ProtobufJsonTranscoder.ProtobufToJson(bytes, desc);

        using var doc = JsonDocument.Parse(output);
        var items = doc.RootElement.GetProperty("items").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, items);
    }

    [Fact]
    public void Nested_message_roundtrips()
    {
        var desc = Kitchen();
        const string INPUT = "{\"inner\":{\"label\":\"deep\"}}";

        var bytes = ProtobufJsonTranscoder.JsonToProtobuf(INPUT, desc);
        var output = ProtobufJsonTranscoder.ProtobufToJson(bytes, desc);

        using var doc = JsonDocument.Parse(output);
        Assert.Equal("deep", doc.RootElement.GetProperty("inner").GetProperty("label").GetString());
    }

    [Fact]
    public void Null_scalar_field_is_omitted_from_encoded_bytes()
    {
        var desc = Kitchen();
        var bytes = ProtobufJsonTranscoder.JsonToProtobuf(
            "{\"name\":null,\"count\":7}", desc);
        var output = ProtobufJsonTranscoder.ProtobufToJson(bytes, desc);

        using var doc = JsonDocument.Parse(output);
        Assert.False(doc.RootElement.TryGetProperty("name", out _));
        Assert.Equal(7, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public void Empty_or_whitespace_json_encodes_to_empty_message()
    {
        var bytes = ProtobufJsonTranscoder.JsonToProtobuf("", HelloRequest.Descriptor);
        Assert.Empty(bytes);

        var bytesWs = ProtobufJsonTranscoder.JsonToProtobuf("   ", HelloRequest.Descriptor);
        Assert.Empty(bytesWs);
    }

    [Fact]
    public void Unknown_field_in_json_is_ignored()
    {
        var bytes = ProtobufJsonTranscoder.JsonToProtobuf(
            "{\"name\":\"x\",\"bogus_field\":99}", HelloRequest.Descriptor);
        var json = ProtobufJsonTranscoder.ProtobufToJson(bytes, HelloRequest.Descriptor);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("x", doc.RootElement.GetProperty("name").GetString());
        Assert.False(doc.RootElement.TryGetProperty("bogus_field", out _));
    }

    [Fact]
    public void String_for_int_field_throws_InvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ProtobufJsonTranscoder.JsonToProtobuf(
                "{\"count\":\"42\"}", Kitchen()));
    }

    [Fact]
    public void Malformed_json_throws_JsonException()
    {
        Assert.ThrowsAny<JsonException>(() =>
            ProtobufJsonTranscoder.JsonToProtobuf("{ not json", HelloRequest.Descriptor));
    }

    [Fact]
    public void Size_cap_throws_BadArgument_for_oversized_payload()
    {
        var huge = $"{{\"name\":\"{new string('a', ProtobufJsonTranscoder.MAX_JSON_BYTES)}\"}}";

        var ex = Assert.Throws<BadArgument>(() =>
            ProtobufJsonTranscoder.JsonToProtobuf(huge, HelloRequest.Descriptor));
        Assert.Contains("exceeds transcoder limit", ex.Detail);
    }

    [Fact]
    public void Deeply_nested_json_is_rejected_by_some_depth_guard()
    {
        var desc = Kitchen();

        var depth = ProtobufJsonTranscoder.MAX_NESTING_DEPTH + 5;
        var payload = string.Concat(Enumerable.Repeat("{\"inner\":", depth))
            + "{\"label\":\"x\"}"
            + new string('}', depth);

        var ex = Assert.ThrowsAny<Exception>(() =>
            ProtobufJsonTranscoder.JsonToProtobuf(payload, desc));
        Assert.True(ex is BadArgument || ex is JsonException,
            $"Expected BadArgument or JsonException, got {ex.GetType().Name}");
    }

    [Fact]
    public void Enum_by_number_encodes_to_corresponding_name()
    {
        var desc = Kitchen();
        var bytes = ProtobufJsonTranscoder.JsonToProtobuf("{\"shade\":2}", desc);
        var output = ProtobufJsonTranscoder.ProtobufToJson(bytes, desc);
        using var doc = JsonDocument.Parse(output);
        Assert.Equal("BLUE", doc.RootElement.GetProperty("shade").GetString());
    }

    [Fact]
    public void Unknown_enum_name_throws_BadArgument()
    {
        var desc = Kitchen();
        var ex = Assert.Throws<BadArgument>(() =>
            ProtobufJsonTranscoder.JsonToProtobuf("{\"shade\":\"PURPLE\"}", desc));
        Assert.Contains("PURPLE", ex.Detail);
    }

    [Fact]
    public void Packed_repeated_int32_from_stock_serializer_decodes_all_elements()
    {
        var desc = Kitchen();
        using var ms = new MemoryStream();
        var cos = new CodedOutputStream(ms);
        using (var payload = new MemoryStream())
        {
            var inner = new CodedOutputStream(payload);
            inner.WriteInt32(10);
            inner.WriteInt32(20);
            inner.WriteInt32(30);
            inner.Flush();
            cos.WriteTag(5, WireFormat.WireType.LengthDelimited);
            cos.WriteBytes(ByteString.CopyFrom(payload.ToArray()));
        }

        cos.Flush();
        var output = ProtobufJsonTranscoder.ProtobufToJson(ms.ToArray(), desc);

        using var doc = JsonDocument.Parse(output);
        var items = doc.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(e => e.GetInt32())
            .ToArray();
        Assert.Equal(new[] { 10, 20, 30 }, items);
    }
}
