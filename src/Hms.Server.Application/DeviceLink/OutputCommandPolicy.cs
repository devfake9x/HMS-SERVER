using System.Text.Json;
using Hms.Server.Domain.Commands;

namespace Hms.Server.Application.DeviceLink;

/// <summary>
/// The server-side gate for commands that move physical outputs (class B, MQTT spec §6.4/§11).
/// The device accepts whatever a key holder signs (spec R11), so a wrong channel or value must be
/// stopped here, before signing. Unknown physical actions fail closed.
/// </summary>
public static class OutputCommandPolicy
{
    public const string PayloadInvalid = "COMMAND_PAYLOAD_INVALID";
    public const string ChannelNotAllowed = "DO_CHANNEL_NOT_ALLOWED";
    public const string PhysicalActionUnsupported = "PHYSICAL_ACTION_UNSUPPORTED";
    public const int DefaultMaxPulseMs = 60_000;

    /// <summary>Returns null when the command may be signed, otherwise the refusal code.</summary>
    /// <param name="allowedChannels">The device's channel allowlist; null or empty allows none.</param>
    /// <param name="maxPulseMs">Server cap on pulse length. The spec leaves the real limit to the
    /// device's HTTP configuration (§6.4), so this is a safety policy, not the device's limit.</param>
    public static string? Check(
        CommandDefinition definition,
        IReadOnlyDictionary<string, object?> payload,
        IReadOnlyList<int>? allowedChannels,
        int maxPulseMs)
    {
        if (definition.Class != CommandClass.Physical)
            return null;

        int channel;
        switch (definition.Type, definition.Action)
        {
            // §6.4: {channel:int>=1, value:bool}
            case ("control", "set_output"):
                if (!HasExactly(payload, "channel", "value") ||
                    !TryInteger(payload["channel"], out channel) || channel < 1 ||
                    !IsBoolean(payload["value"]))
                    return PayloadInvalid;
                break;

            // §6.4: {channel, duration_ms}; the channel must be in pulse mode on the device.
            case ("control", "pulse"):
                if (!HasExactly(payload, "channel", "duration_ms") ||
                    !TryInteger(payload["channel"], out channel) || channel < 1 ||
                    !TryInteger(payload["duration_ms"], out var duration) || duration < 1 || duration > maxPulseMs)
                    return PayloadInvalid;
                break;

            default:
                return PhysicalActionUnsupported;
        }

        return allowedChannels is { Count: > 0 } && allowedChannels.Contains(channel) ? null : ChannelNotAllowed;
    }

    private static bool HasExactly(IReadOnlyDictionary<string, object?> payload, params string[] keys) =>
        payload.Count == keys.Length && keys.All(payload.ContainsKey);

    // Only true integers: no strings, no fractions, no booleans-as-numbers.
    private static bool TryInteger(object? value, out int result)
    {
        result = 0;
        switch (value)
        {
            case JsonElement { ValueKind: JsonValueKind.Number } element:
                return element.TryGetInt32(out result);
            case int i:
                result = i;
                return true;
            case long l and >= int.MinValue and <= int.MaxValue:
                result = (int)l;
                return true;
            case short s:
                result = s;
                return true;
            case byte b:
                result = b;
                return true;
            default:
                return false;
        }
    }

    // The spec accepts 0/1 too, but asks clients to send booleans; numbers are refused so a
    // typed-in channel number can never be read as a value.
    private static bool IsBoolean(object? value) =>
        value is bool || value is JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False };
}
