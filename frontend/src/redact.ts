const SECRET_KEYS = new Set([
  'password',
  'commandkey',
  'command_key_base64',
  'token',
  'access_token',
  'refresh_token',
  'privatekey',
  'private_key',
  'rawenvelope',
  'raw_envelope',
  'bundle',
  'license',
])

export function redactSecrets<T>(value: T): T {
  if (Array.isArray(value)) return value.map((item) => redactSecrets(item)) as T
  if (!value || typeof value !== 'object') return value

  const result: Record<string, unknown> = {}
  for (const [key, item] of Object.entries(value as Record<string, unknown>)) {
    result[key] = SECRET_KEYS.has(key.toLowerCase()) ? '[REDACTED]' : redactSecrets(item)
  }
  return result as T
}
