import { describe, expect, it, vi } from 'vitest'
import { downloadSecretBlob } from './secureDownload'

describe('downloadSecretBlob', () => {
  it('rejects responses without no-store and no-cache before reading bytes', async () => {
    const blob = vi.fn()
    const fetchImpl = vi.fn().mockResolvedValue(new Response('secret', { status: 200 }))
    await expect(downloadSecretBlob({ endpoint: '/api/device/bundle', filename: 'bundle.bin', fetchImpl, documentImpl: { createElement: vi.fn(), body: { appendChild: vi.fn() } } as unknown as Document })).rejects.toThrow('disable caching')
    expect(blob).not.toHaveBeenCalled()
  })

  it('sends CSRF, downloads same-origin bytes and revokes the object URL', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(new Response('secret', { status: 200, headers: { 'Cache-Control': 'private, no-store', Pragma: 'no-cache' } }))
    const createObjectURL = vi.fn().mockReturnValue('blob:test')
    const revokeObjectURL = vi.fn()
    const click = vi.fn()
    const anchor = { href: '', download: '', click, remove: vi.fn() } as unknown as HTMLAnchorElement
    const documentImpl = { createElement: vi.fn().mockReturnValue(anchor), body: { appendChild: vi.fn() } } as unknown as Document
    await downloadSecretBlob({ endpoint: '/api/device/bundle', filename: 'bundle.bin', csrfToken: 'csrf', fetchImpl, urlApi: { createObjectURL, revokeObjectURL }, documentImpl })
    expect(fetchImpl).toHaveBeenCalledWith(expect.any(URL), expect.objectContaining({ headers: { 'X-CSRF-TOKEN': 'csrf' } }))
    expect(click).toHaveBeenCalled()
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:test')
  })

  it('passes an abort signal to the one-time download request', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(new Response('secret', { status: 200, headers: { 'Cache-Control': 'no-store', Pragma: 'no-cache' } }))
    const controller = new AbortController()
    const anchor = { href: '', download: '', click: vi.fn(), remove: vi.fn() } as unknown as HTMLAnchorElement
    await downloadSecretBlob({
      endpoint: '/api/device/bundle', filename: 'bundle.bin', fetchImpl, signal: controller.signal,
      urlApi: { createObjectURL: vi.fn().mockReturnValue('blob:test'), revokeObjectURL: vi.fn() },
      documentImpl: { createElement: vi.fn().mockReturnValue(anchor), body: { appendChild: vi.fn() } } as unknown as Document,
    })
    expect((fetchImpl.mock.calls[0][1] as RequestInit).signal).toBe(controller.signal)
    expect((fetchImpl.mock.calls[0][1] as RequestInit).cache).toBe('no-store')
  })

  it('rejects an artifact that exceeds the configured byte limit before reading it', async () => {
    const blob = vi.fn()
    const response = new Response('secret', { status: 200, headers: { 'Cache-Control': 'no-store', Pragma: 'no-cache', 'Content-Length': '20' } })
    response.blob = blob
    await expect(downloadSecretBlob({ endpoint: '/api/device/bundle', filename: 'x', maxBytes: 10, fetchImpl: vi.fn().mockResolvedValue(response) })).rejects.toThrow('size limit')
    expect(blob).not.toHaveBeenCalled()
  })

  it('validates an expected artifact content type before reading bytes', async () => {
    const blob = vi.fn()
    const response = new Response('secret', { status: 200, headers: { 'Cache-Control': 'no-store', Pragma: 'no-cache', 'Content-Type': 'text/plain' } })
    response.blob = blob
    await expect(downloadSecretBlob({ endpoint: '/api/device/bundle', filename: 'x', expectedContentType: 'application/octet-stream', fetchImpl: vi.fn().mockResolvedValue(response) })).rejects.toThrow('content type')
    expect(blob).not.toHaveBeenCalled()
  })

  it('rejects cross-origin endpoints', async () => {
    await expect(downloadSecretBlob({ endpoint: 'https://evil.invalid/bundle', filename: 'x' })).rejects.toThrow('same-origin')
  })
})
