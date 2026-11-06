using System.Text.Json;

namespace Hms.Server.Application.DeviceLink;

/// <summary>Strict readers for command payloads, which arrive as API JSON (<see cref="JsonElement"/>) or
/// as plain values from server code. No conversions: a string is never read as a number, and a
/// number never as a boolean.</summary>
internal static class CommandPayloadRules
{
    public static bool HasExactly(IReadOnlyDictionary<string, object?> payload, params string[] keys) =>
        payload.Count == keys.Length && keys.All(payload.ContainsKey);

    /// <summary>Every required key is present and no key outside required and optional is.</summary>
    public static bool HasKeys(IReadOnlyDictionary<string, object?> payload, string[] required, string[] optional) =>
        required.All(payload.ContainsKey) && payload.Keys.All(x => required.Contains(x) || optional.Contains(x));

    // Only true integers: no strings, no fractions, no booleans-as-numbers.
    public static bool TryInteger(object? value, out int result)
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

    public static bool IsBoolean(object? value) =>
        value is bool || value is JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False };

    public static bool TryString(object? value, out string result)
    {
        switch (value)
        {
            case string s:
                result = s;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } element:
                try
                {
                    result = element.GetString()!;
                    return true;
                }
                catch (InvalidOperationException)
                {
                    // An escape that is not valid UTF-16, such as a lone "\uD800": a bad payload.
                    result = string.Empty;
                    return false;
                }
            default:
                result = string.Empty;
                return false;
        }
    }
}
