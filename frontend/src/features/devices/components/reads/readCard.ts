import type { CommandCatalogSnapshot, DeviceLinkDto } from '@/core/api/adminApi'

/** What every read card needs from the device page. */
export interface ReadCardContext {
  deviceId: string
  csrfToken: string | null
  catalog: CommandCatalogSnapshot | null
  catalogVerified: boolean
  allowedCommands: string[] | null
  link: DeviceLinkDto | null
}
