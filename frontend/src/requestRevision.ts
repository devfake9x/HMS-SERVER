export interface RequestRevision {
  next(): number
  isCurrent(revision: number): boolean
}

export function createRequestRevision(): RequestRevision {
  let current = 0
  return {
    next: () => ++current,
    isCurrent: revision => revision === current,
  }
}
