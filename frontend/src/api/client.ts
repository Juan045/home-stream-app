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
}

export function errorMessage(slug: string): string {
  return MESSAGES[slug] ?? 'Fallo el procesamiento del video.'
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

export async function get<T>(url: string): Promise<T> {
  return unwrap<T>(await fetch(url))
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
