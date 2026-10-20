import { describe, expect, it } from 'vitest'
import { createSessionFence } from './sessionFence'

describe('session fence', () => {
  it('rejects a delayed response after the session rotates', async () => {
    const fence = createSessionFence()
    const userA = fence.capture()
    let resolve!: (value: string) => void
    const delayed = new Promise<string>(done => { resolve = done })

    const accepted = delayed.then(value => fence.isCurrent(userA) ? value : null)
    fence.rotate()
    resolve('user-a-data')

    await expect(accepted).resolves.toBeNull()
  })

  it('aborts the previous request and exposes a fresh signal', () => {
    const fence = createSessionFence()
    const first = fence.capture()
    const second = fence.rotate()

    expect(first.signal.aborted).toBe(true)
    expect(second.signal.aborted).toBe(false)
    expect(fence.isCurrent(first)).toBe(false)
    expect(fence.isCurrent(second)).toBe(true)
  })
})
