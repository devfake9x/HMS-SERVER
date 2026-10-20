import { describe, expect, it } from 'vitest'
import { createRequestRevision } from './requestRevision'

describe('createRequestRevision', () => {
  it('invalidates an older request when a newer request starts', () => {
    const revision = createRequestRevision()
    const first = revision.next()
    const second = revision.next()

    expect(revision.isCurrent(first)).toBe(false)
    expect(revision.isCurrent(second)).toBe(true)
  })

  it('keeps a request current until another request starts', () => {
    const revision = createRequestRevision()
    const current = revision.next()

    expect(revision.isCurrent(current)).toBe(true)
  })
})
