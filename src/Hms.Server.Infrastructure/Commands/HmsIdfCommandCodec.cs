using System.Buffers;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Hms.Server.Application.DeviceLink;

namespace Hms.Server.Infrastructure.Commands;

/// <summary>
/// The HMS-IDF command and response wire format (MQTT spec §3, §4, §5): one compact UTF-8 JSON
/// line, HMAC-SHA256 over the exact bytes sent, <c>sig</c> appended as the last field, at most
/// 1023 bytes in total. Responses are signed over <c>"res:" + body-without-sig</c>.
/// </summary>
public static class HmsIdfCommandCodec
{
    public const int MaxCommandBytes = 1023;
    public const int KeyBytes = 32;
    // Spec §3: nesting deeper than 16 is refused by the device.
    private const int MaxNesting = 16;
    private const int SigHexLength = 64;
    private static ReadOnlySpan<byte> SigFieldPrefix => ",\"sig\":\""u8;
    private static ReadOnlySpan<byte> ResponseDomain => "res:"u8;

    // Raw UTF-8 instead of \u escapes: the device verifies the bytes it receives, and spec §4.2
    // signs with ensure_ascii=False.
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        MaxDepth = MaxNesting
    };

    public sealed record CommandEnvelope(
        string Type,
        string Id,
        long Timestamp,
        long TtlMs,
        string Action,
        IReadOnlyDictionary<string, object?>? Parameters = null,
        uint? KeyVersion = null,
        string? Confirm = null,
        bool DryRun = false);

    /// <summary>The unsigned envelope. Root fields are the spec §3 allowlist only; parameters go
    /// in <c>data.data</c> and are omitted when empty.</summary>
    public static byte[] Serialize(CommandEnvelope envelope)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("type", envelope.Type);
            writer.WriteString("id", envelope.Id);
            writer.WriteNumber("ts", envelope.Timestamp);
            writer.WriteNumber("ttl_ms", envelope.TtlMs);
            if (envelope.KeyVersion is { } keyVersion)
                writer.WriteNumber("key_ver", keyVersion);
            if (envelope.Confirm is not null)
                writer.WriteString("confirm", envelope.Confirm);
            if (envelope.DryRun)
                writer.WriteBoolean("dry_run", true);
            writer.WritePropertyName("data");
            writer.WriteStartObject();
            writer.WriteString("action", envelope.Action);
            if (envelope.Parameters is { Count: > 0 } parameters)
            {
                writer.WritePropertyName("data");
                JsonSerializer.Serialize(writer, parameters);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        var json = buffer.WrittenSpan.ToArray();
        if (!HasOnlyAcceptedStructure(json))
            throw new InvalidOperationException("COMMAND_PAYLOAD_INVALID");
        return json;
    }

    /// <summary>Spec §4.1: HMAC the unsigned bytes, drop the final <c>}</c>, append
    /// <c>,"sig":"&lt;hex&gt;"}</c>. The result is sent as is.</summary>
    public static byte[] Sign(ReadOnlySpan<byte> unsignedJson, ReadOnlySpan<byte> commandKey)
    {
        if (commandKey.Length != KeyBytes)
            throw new ArgumentException("COMMAND_KEY_INVALID", nameof(commandKey));
        if (unsignedJson.Length < 2 || unsignedJson[^1] != (byte)'}')
            throw new ArgumentException("COMMAND_PAYLOAD_INVALID", nameof(unsignedJson));

        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(commandKey, unsignedJson, mac);
        var hex = System.Text.Encoding.ASCII.GetBytes(Convert.ToHexStringLower(mac));

        var wire = new byte[unsignedJson.Length - 1 + SigFieldPrefix.Length + hex.Length + 2];
        var cursor = 0;
        unsignedJson[..^1].CopyTo(wire);
        cursor += unsignedJson.Length - 1;
        SigFieldPrefix.CopyTo(wire.AsSpan(cursor));
        cursor += SigFieldPrefix.Length;
        hex.CopyTo(wire.AsSpan(cursor));
        cursor += hex.Length;
        wire[cursor++] = (byte)'"';
        wire[cursor] = (byte)'}';
        return wire;
    }

    /// <summary>The root <c>id</c> string of a reply, read without verifying it.</summary>
    public static string? PeekId(ReadOnlySpan<byte> wire)
    {
        try
        {
            var reader = new Utf8JsonReader(TrimTrailing(wire));
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return null;
            while (reader.Read() && reader.CurrentDepth > 0)
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1)
                    continue;
                var isId = reader.ValueTextEquals("id"u8);
                if (!reader.Read())
                    return null;
                if (isId)
                    return reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                reader.Skip();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>Spec §4.1/§4.2: the reply must end with <c>,"sig":"&lt;64 hex&gt;"}</c> and the HMAC
    /// over <c>"res:" + body</c> must match; anything else is rejected.</summary>
    public static DeviceCommandResponse? VerifyResponse(ReadOnlySpan<byte> wire, ReadOnlySpan<byte> commandKey)
    {
        if (commandKey.Length != KeyBytes)
            return null;
        wire = TrimTrailing(wire);
        var tailLength = SigFieldPrefix.Length + SigHexLength + 2;
        if (wire.Length <= tailLength + 1)
            return null;
        var tail = wire[^tailLength..];
        if (!tail.StartsWith(SigFieldPrefix) || tail[^2] != (byte)'"' || tail[^1] != (byte)'}')
            return null;
        var hex = tail.Slice(SigFieldPrefix.Length, SigHexLength);
        foreach (var b in hex)
        {
            if (!char.IsAsciiHexDigit((char)b))
                return null;
        }

        var body = new byte[wire.Length - tailLength + 1];
        wire[..^tailLength].CopyTo(body);
        body[^1] = (byte)'}';

        var signed = new byte[ResponseDomain.Length + body.Length];
        ResponseDomain.CopyTo(signed);
        body.CopyTo(signed.AsSpan(ResponseDomain.Length));
        Span<byte> expected = stackalloc byte[32];
        HMACSHA256.HashData(commandKey, signed, expected);
        var provided = Convert.FromHexString(System.Text.Encoding.ASCII.GetString(hex));
        if (!CryptographicOperations.FixedTimeEquals(expected, provided))
            return null;

        return ParseResponse(body);
    }

    private static DeviceCommandResponse? ParseResponse(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("success", out var success) ||
                success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return null;

            return new DeviceCommandResponse(
                id.GetString()!,
                type.GetString()!,
                root.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.String ? action.GetString() : null,
                success.GetBoolean(),
                root.TryGetProperty("ts", out var ts) && ts.TryGetInt64(out var seconds) ? seconds : null,
                root.TryGetProperty("key_ver", out var keyVer) && keyVer.TryGetUInt32(out var version) ? version : null,
                root.TryGetProperty("data", out var data) ? data.Clone() : null,
                root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString() : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Spec §3: no duplicate keys (case-insensitive), no NUL, nesting within the writer's limit.
    private static bool HasOnlyAcceptedStructure(byte[] json)
    {
        var reader = new Utf8JsonReader(json);
        var scopes = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    scopes.Push(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    break;
                case JsonTokenType.EndObject:
                    scopes.Pop();
                    break;
                case JsonTokenType.PropertyName:
                    var name = reader.GetString()!;
                    if (name.Contains('\0') || !scopes.Peek().Add(name))
                        return false;
                    break;
                case JsonTokenType.String:
                    if (reader.GetString()!.Contains('\0'))
                        return false;
                    break;
            }
        }

        return true;
    }

    private static ReadOnlySpan<byte> TrimTrailing(ReadOnlySpan<byte> wire)
    {
        var end = wire.Length;
        while (end > 0 && wire[end - 1] is 0 or (byte)'\n' or (byte)'\r' or (byte)' ' or (byte)'\t')
            end--;
        return wire[..end];
    }
}

public sealed class HmsIdfResponseCodec : IDeviceResponseCodec
{
    public string? PeekId(ReadOnlySpan<byte> wire) => HmsIdfCommandCodec.PeekId(wire);

    public DeviceCommandResponse? Verify(ReadOnlySpan<byte> wire, ReadOnlySpan<byte> commandKey) =>
        HmsIdfCommandCodec.VerifyResponse(wire, commandKey);
}
