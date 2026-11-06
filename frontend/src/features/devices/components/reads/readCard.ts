import type { CommandCatalogSnapshot, DeviceLinkDto } from '@/core/api/adminApi'
import type { ReadState, ReadStep } from '@/core/commands/useDeviceReads'

/** What every read card needs from the device page. */
export interface ReadCardContext {
  deviceId: string
  csrfToken: string | null
  catalog: CommandCatalogSnapshot | null
  catalogVerified: boolean
  allowedCommands: string[] | null
  link: DeviceLinkDto | null
  /** Set by the page when someone else changed this card's configuration: reads older than this are stale. */
  configChangedAt?: number | null
}

/** Reads these steps again, even while the card is reading; resolves with their states afterwards. */
export type RefreshReads = (steps: ReadStep[]) => Promise<Record<string, ReadState>>

/** What a card passes into the editors it renders in its slot. */
export interface ConfigEditorProps extends ReadCardContext {
  states: Record<string, ReadState>
  now: number
  refresh: RefreshReads
  lockReads: (on: boolean) => void
}
