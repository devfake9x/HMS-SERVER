export interface SessionLease {
  revision: number
  signal: AbortSignal
}

export interface SessionFence {
  capture(): SessionLease
  rotate(): SessionLease
  invalidate(): void
  isCurrent(lease: SessionLease): boolean
}

/**
 * Couples a monotonically increasing session revision to an AbortSignal. A
 * response must pass isCurrent() before it can update authenticated state.
 */
export function createSessionFence(): SessionFence {
  let revision = 0
  let controller = new AbortController()

  const capture = (): SessionLease => ({ revision, signal: controller.signal })

  return {
    capture,
    rotate: () => {
      controller.abort()
      revision++
      controller = new AbortController()
      return capture()
    },
    invalidate: () => {
      controller.abort()
      revision++
      controller = new AbortController()
    },
    isCurrent: lease => lease.revision === revision && lease.signal === controller.signal && !lease.signal.aborted,
  }
}
