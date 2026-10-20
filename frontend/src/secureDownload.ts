export interface SecureDownloadOptions {
  endpoint: string
  method?: 'GET' | 'POST'
  csrfToken?: string
  filename: string
  fetchImpl?: typeof fetch
  signal?: AbortSignal
  urlApi?: Pick<typeof URL, 'createObjectURL' | 'revokeObjectURL'>
  documentImpl?: Pick<Document, 'createElement' | 'body'>
  maxBytes?: number
  expectedContentType?: string | RegExp
}

export async function downloadSecretBlob(options: SecureDownloadOptions): Promise<void> {
  const origin = globalThis.location?.origin ?? 'http://localhost'
  const url = new URL(options.endpoint, origin)
  if (url.origin !== origin) throw new Error('Secret downloads must be same-origin')
  const fetchImpl = options.fetchImpl ?? fetch
  const response = await fetchImpl(url, {
    method: options.method ?? 'POST',
    credentials: 'same-origin',
    headers: options.csrfToken ? { 'X-CSRF-TOKEN': options.csrfToken } : undefined,
    cache: 'no-store',
    signal: options.signal,
  })
  const cacheControl = response.headers.get('cache-control')?.toLowerCase() ?? ''
  const pragma = response.headers.get('pragma')?.toLowerCase() ?? ''
  if (!cacheControl.includes('no-store') || !pragma.includes('no-cache')) {
    throw new Error('Secret download response must disable caching')
  }
  if (!response.ok) throw new Error(`Secret download failed (${response.status})`)
  if (options.expectedContentType) {
    const contentType = response.headers.get('content-type')?.split(';', 1)[0].trim().toLowerCase() ?? ''
    const expected = options.expectedContentType
    const matches = typeof expected === 'string'
      ? contentType === expected.toLowerCase()
      : expected.test(contentType)
    if (!matches) throw new Error('Secret download content type is invalid')
  }
  const maxBytes = options.maxBytes ?? 1024 * 1024
  const declaredLength = Number(response.headers.get('content-length'))
  if (Number.isFinite(declaredLength) && declaredLength > maxBytes) {
    throw new Error('Secret download exceeds size limit')
  }
  const blob = await response.blob()
  if (blob.size > maxBytes) throw new Error('Secret download exceeds size limit')
  const urlApi = options.urlApi ?? URL
  const objectUrl = urlApi.createObjectURL(blob)
  try {
    const documentImpl = options.documentImpl ?? globalThis.document
    if (!documentImpl) throw new Error('Document is unavailable for secret download')
    const anchor = documentImpl.createElement('a')
    anchor.href = objectUrl
    anchor.download = options.filename
    documentImpl.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
  } finally {
    urlApi.revokeObjectURL(objectUrl)
  }
}
