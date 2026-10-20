import { catalogIsCurrent } from './permissions'

export interface CatalogAction {
  type: string
  action: string
  available: boolean
  dangerous: boolean
  requiresConfirmation: boolean
  featurePermission: string
}

export function canRenderCommand(
  action: CatalogAction | null | undefined,
  expectedCatalog: { version: string; hash: string } | null | undefined,
  actualCatalog: { version: string; hash: string } | null | undefined,
): boolean {
  return !!action && action.available &&
    catalogIsCurrent(expectedCatalog, actualCatalog)
}

export function findCatalogAction(
  actions: readonly CatalogAction[] | null | undefined,
  type: string,
  action: string,
): CatalogAction | null {
  return actions?.find(candidate => candidate.type === type && candidate.action === action) ?? null
}

export function requiresCommandConfirmation(action: CatalogAction): boolean {
  return action.dangerous || action.requiresConfirmation
}

export type CommandViewState = 'pending' | 'deferred' | 'acknowledged' | 'failed' | 'revoked'

export function commandViewState(status: string): CommandViewState {
  switch (status) {
    case 'MQTT_DEFERRED':
    case 'TRANSPORT_NOT_CONFIGURED':
    case 'CONTRACT_UNAVAILABLE':
      return 'deferred'
    case 'ACKNOWLEDGED':
      return 'acknowledged'
    case 'DEVICE_REVOKED':
      return 'revoked'
    case 'PENDING':
      return 'pending'
    default:
      return 'failed'
  }
}
