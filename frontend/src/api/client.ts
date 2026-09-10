/**
 * Cliente tipado de la API.
 *
 * Los tipos salen de types.ts, que se genera del /openapi.json de FastAPI.
 * Nunca escribirlos a mano: si cambia un campo en app/models/schemas.py esto
 * tiene que dejar de compilar.
 */
import type { components } from './types'

export type StreamResponse = components['schemas']['StreamResponse']
export type AudioTrack = components['schemas']['AudioTrackSchema']
export type SubtitleTrack = components['schemas']['SubtitleTrackSchema']
export type MediaListItem = components['schemas']['MediaListItem']
export type MediaListResponse = components['schemas']['MediaListResponse']
export type MediaResponse = components['schemas']['MediaResponse']

/** El backend responde siempre {"error": slug, "detail": texto}. */
export class ApiError extends Error {
  slug: string
  status: number

  constructor(slug: string, status: number) {
    super(slug)
    this.name = 'ApiError'
    this.slug = slug
    this.status = status
  }
}

// El contrato estable es el slug. El detail es prosa y esta siendo saneado
// (F3 del plan de hardening), asi que no se muestra nunca.
const MESSAGES: Record<string, string> = {
  invalid_path: 'La ruta tiene que ser absoluta.',
  path_traversal: 'La ruta no es valida.',
  unsupported_extension: 'Formato no soportado. Solo .mp4 y .mkv.',
  file_not_found: 'No existe ese archivo.',
  outside_media_root: 'El archivo esta fuera del directorio permitido.',
  too_many_jobs: 'El servidor esta procesando otros videos. Reintentar en unos segundos.',
  storage_limit: 'El cache esta lleno y no hay nada que liberar.',
  session_not_found: 'La sesion expiro o no existe.',
  asset_not_found: 'El video ya no esta disponible.',
  playlist_not_ready: 'Todavia se esta generando.',
  track_not_found: 'Esa pista no existe.',
  media_not_found: 'Esa ficha ya no existe.',
  media_root_not_configured: 'El servidor arranco sin SM_MEDIA_ROOT: el catalogo no esta disponible.',
}

/** El fallback cubre tambien el caso sin slug: `fetch` rechaza sin respuesta
 *  cuando el servidor no esta, y ahi lo unico cierto es que no se pudo. */
export function errorMessage(slug: string): string {
  return MESSAGES[slug] ?? 'No se pudo conectar con el servidor.'
}

async function unwrap<T>(resp: Response): Promise<T> {
  if (!resp.ok) {
    const data: unknown = await resp.json().catch(() => null)
    const slug =
      typeof data === 'object' && data !== null && 'error' in data
        ? String((data as { error: unknown }).error)
        : 'unknown'
    throw new ApiError(slug, resp.status)
  }
  return (await resp.json()) as T
}

export async function get<T>(url: string, signal?: AbortSignal): Promise<T> {
  return unwrap<T>(await fetch(url, { signal }))
}

export async function post<T>(url: string, body: unknown): Promise<T> {
  const resp = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  return unwrap<T>(resp)
}

export function openStream(filePath: string): Promise<StreamResponse> {
  return post<StreamResponse>('/api/v1/stream', { file_path: filePath })
}

export function readSession(sessionId: string): Promise<StreamResponse> {
  return get<StreamResponse>(`/api/v1/sessions/${sessionId}`)
}

/** Los filtros de `GET /media`. No hay filtro por genero: la API no lo tiene. */
export interface MediaQuery {
  q?: string
  kind?: 'film' | 'series' | 'documentary'
  in_list?: boolean
  sort?: 'title' | 'added'
  limit?: number
  offset?: number
}

export function listMedia(
  query: MediaQuery = {},
  signal?: AbortSignal,
): Promise<MediaListResponse> {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== '') params.set(key, String(value))
  }
  return get<MediaListResponse>(`/api/v1/media?${params}`, signal)
}

export function readMedia(
  idMedia: string,
  signal?: AbortSignal,
): Promise<MediaResponse> {
  return get<MediaResponse>(`/api/v1/media/${encodeURIComponent(idMedia)}`, signal)
}
