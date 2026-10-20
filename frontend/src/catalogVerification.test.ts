import { describe, expect, it } from 'vitest'
import { createCatalogVerification, verifyCatalog, invalidateCatalogVerification } from './catalogVerification'

const first = { trusted: true, catalogVersion: 'v1', catalogHash: 'hash1' }
function pending<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}

describe('independent catalog verification', () => {
  it('stores a separate identity and clears proof on request failure', async () => {
    const state = createCatalogVerification()
    expect(await verifyCatalog(state, first, async () => first, true)).toBe(true)
    expect(state.verified).toEqual({ version: 'v1', hash: 'hash1' })
    expect(await verifyCatalog(state, first, async () => { throw new Error('offline') })).toBe(false)
    expect(state.verified).toBeNull()
  })

  it('latches changed catalog until an explicit reload', async () => {
    const state = createCatalogVerification()
    const changed = { ...first, catalogVersion: 'v2' }
    await verifyCatalog(state, first, async () => first, true)
    expect(await verifyCatalog(state, first, async () => changed)).toBe(false)
    expect(state.reloadRequired).toBe(true)
    expect(await verifyCatalog(state, changed, async () => changed)).toBe(false)
    expect(state.verified).toBeNull()
    expect(await verifyCatalog(state, null, async () => changed, true)).toBe(true)
  })

  it('does not let an earlier success overwrite a later failure', async () => {
    const state = createCatalogVerification()
    const earlier = pending<typeof first>()
    const result = verifyCatalog(state, first, () => earlier.promise)
    await verifyCatalog(state, first, async () => { throw new Error('offline') })
    earlier.resolve(first)
    expect(await result).toBe(false)
    expect(state.verified).toBeNull()
  })

  it('logout invalidates pending reload and verification responses', async () => {
    const state = createCatalogVerification()
    const request = pending<typeof first>()
    const result = verifyCatalog(state, null, () => request.promise, true)
    invalidateCatalogVerification(state)
    request.resolve(first)
    expect(await result).toBe(false)
    expect(state.verified).toBeNull()
    expect(state.loaded).toBeNull()
  })

  it('rejects untrusted or blank identities', async () => {
    for (const catalog of [{ ...first, trusted: false }, { ...first, catalogHash: ' ' }, { ...first, catalogVersion: '' }]) {
      const state = createCatalogVerification()
      expect(await verifyCatalog(state, null, async () => catalog, true)).toBe(false)
      expect(state.verified).toBeNull()
    }
  })
})
