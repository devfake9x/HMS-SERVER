using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hms.Server.Domain.Commands;

namespace Hms.Server.Application.DeviceLink;

/// <summary>
/// The server-side gate for configuration writes (classes W, C, D, F; MQTT spec §6.1/§6.2). The device
/// takes whatever a key holder signs (spec R11), so each opened write has an exact payload rule here,
/// checked before signing. A write without a rule fails closed, even if the catalog opened it.
/// </summary>
public static partial class ConfigCommandPolicy
{
    public const string PayloadInvalid = OutputCommandPolicy.PayloadInvalid;
    public const string ActionUnsupported = "CONFIG_ACTION_UNSUPPORTED";

    /// <summary>POSIX TZ strings the server lets an operator set. The spec does not define the
    /// format the device takes, so only values seen working on the device are listed: the lab
    /// gateway reports ICT-7, and UTC0 is the plain UTC form.</summary>
    public static IReadOnlySet<string> SupportedTimezones { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "ICT-7", "UTC0" };

    private const int MaxHostnameBytes = 63;
    private const int MaxChannelNameBytes = 32;
    private const int MaxChannelTypeBytes = 32;
    private const int MinAnalogInput = 1;
    private const int MaxAnalogInput = 16;

    /// <summary>Returns null when the write may be signed, otherwise the refusal code.</summary>
    public static string? Check(CommandDefinition definition, IReadOnlyDictionary<string, object?> payload)
    {
        if (definition.Class is CommandClass.Read or CommandClass.Physical)
            return null;

        var valid = (definition.Type, definition.Action) switch
        {
            // §6.1: {hostname:string}, "nên giữ hostname <=63 byte"; held to a DNS label.
            ("system", "set_hostname") =>
                CommandPayloadRules.HasExactly(payload, "hostname") &&
                CommandPayloadRules.TryString(payload["hostname"], out var hostname) &&
                Encoding.UTF8.GetByteCount(hostname) <= MaxHostnameBytes &&
                HostnamePattern().IsMatch(hostname),

            // §6.1: {timezone:string}, a TZ string the device accepts.
            ("system", "set_timezone") =>
                CommandPayloadRules.HasExactly(payload, "timezone") &&
                CommandPayloadRules.TryString(payload["timezone"], out var timezone) &&
                SupportedTimezones.Contains(timezone),

            // §6.1: no input; it only triggers a sync with the configured servers.
            ("system", "sync_ntp") => payload.Count == 0,

            // §6.1: {delay_ms?: 0..60000}, default 2000. The server sends none: with the default it
            // knows the deadline (DeviceRebootCodes.DeviceDelay), and delay_ms:0 could restart the
            // device before its reply leaves, which would leave the command with no outcome.
            ("system", "reboot") => payload.Count == 0,

            // §6.2: {ai:1..16, name?, type?, enabled?}. The name is required here: the UI renames,
            // and sends back the type and enabled state it read so an omitted field cannot reset.
            ("adc", "set_channel") =>
                CommandPayloadRules.HasKeys(payload, ["ai", "name"], ["type", "enabled"]) &&
                IsAnalogInput(payload["ai"]) &&
                IsChannelName(payload["name"]) &&
                (!payload.TryGetValue("type", out var type) || IsChannelType(type)) &&
                (!payload.TryGetValue("enabled", out var enabled) || CommandPayloadRules.IsBoolean(enabled)),

            // §6.2: {ai:1..16, enabled:bool}.
            ("adc", "toggle_channel") =>
                CommandPayloadRules.HasExactly(payload, "ai", "enabled") &&
                IsAnalogInput(payload["ai"]) &&
                CommandPayloadRules.IsBoolean(payload["enabled"]),

            _ => (bool?)null
        };

        return valid switch
        {
            null => ActionUnsupported,
            true => null,
            false => PayloadInvalid
        };
    }

    private static bool IsAnalogInput(object? value) =>
        CommandPayloadRules.TryInteger(value, out var ai) && ai is >= MinAnalogInput and <= MaxAnalogInput;

    // Free text, so it may hold any language, but no control or format characters: a bidi override
    // (U+202E) or a zero-width space would make one channel look like another in the UI.
    private static bool IsChannelName(object? value) =>
        CommandPayloadRules.TryString(value, out var name) &&
        !string.IsNullOrWhiteSpace(name) &&
        IsWellFormedUtf16(name) &&
        Encoding.UTF8.GetByteCount(name) <= MaxChannelNameBytes &&
        name.EnumerateRunes().All(rune => !IsInvisible(rune) && Rune.GetUnicodeCategory(rune) is not (
            UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or
            UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned));

    // Letters, symbols and marks that render as nothing: Hangul fillers, the braille blank, the
    // combining grapheme joiner, Khmer inherent vowels and the variation selectors.
    private static bool IsInvisible(Rune rune) => rune.Value is
        0x034F or 0x115F or 0x1160 or 0x17B4 or 0x17B5 or 0x2800 or 0x3164 or 0xFFA0 or
        (>= 0x180B and <= 0x180F) or (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0100 and <= 0xE01EF);

    // EnumerateRunes turns a lone surrogate into U+FFFD, so it must be refused before: the device
    // would get bytes the server never checked.
    private static bool IsWellFormedUtf16(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                i++;
            else if (char.IsSurrogate(text[i]))
                return false;
        }

        return true;
    }

    // The spec lists no type values; the UI only sends back the value the device reported.
    private static bool IsChannelType(object? value) =>
        CommandPayloadRules.TryString(value, out var type) &&
        Encoding.UTF8.GetByteCount(type) <= MaxChannelTypeBytes &&
        ChannelTypePattern().IsMatch(type);

    // \A and \z, not ^ and $: "$" also matches before a trailing newline.
    [GeneratedRegex(@"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex HostnamePattern();

    [GeneratedRegex(@"\A[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex ChannelTypePattern();
}
