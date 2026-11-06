/**
 * Timezones the server accepts for system.set_timezone. This is a mirror for the picker only:
 * the server decides (src/Hms.Server.Application/DeviceLink/ConfigCommandPolicy.cs,
 * SupportedTimezones) and answers COMMAND_PAYLOAD_INVALID for anything else.
 */
export const TIMEZONES: readonly string[] = ['ICT-7', 'UTC0']
