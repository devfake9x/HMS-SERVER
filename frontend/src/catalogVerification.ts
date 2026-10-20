interface CatalogMetadata { trusted: boolean; catalogVersion: string; catalogHash: string }
export interface CatalogVerification<T extends CatalogMetadata> {
  revision: number
  verified: { version: string; hash: string } | null
  loaded: T | null
  reloadRequired: boolean
}

export function createCatalogVerification<T extends CatalogMetadata = CatalogMetadata>(): CatalogVerification<T> {
  return { revision: 0, verified: null, loaded: null, reloadRequired: false }
}

export function invalidateCatalogVerification<T extends CatalogMetadata>(state: CatalogVerification<T>): void {
  state.revision++
  state.verified = null
  state.loaded = null
  state.reloadRequired = true
}

export async function verifyCatalog<T extends CatalogMetadata>(
  state: CatalogVerification<T>, expected: T | null, fetchCatalog: () => Promise<T>, reload = false,
): Promise<boolean> {
  const revision = ++state.revision
  state.verified = null
  if (!reload && state.reloadRequired) return false
  // Copy identity before awaiting so mutable UI selection is not its own proof.
  const version = expected?.catalogVersion
  const hash = expected?.catalogHash
  try {
    const latest = await fetchCatalog()
    if (revision !== state.revision) return false
    if (!latest.trusted || !latest.catalogVersion.trim() || !latest.catalogHash.trim() ||
      (!reload && (version !== latest.catalogVersion || hash !== latest.catalogHash))) {
      state.reloadRequired = true
      return false
    }
    state.verified = { version: latest.catalogVersion, hash: latest.catalogHash }
    if (reload) state.loaded = latest
    state.reloadRequired = false
    return true
  } catch {
    if (revision === state.revision) state.verified = null
    return false
  }
}
