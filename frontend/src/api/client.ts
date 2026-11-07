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
export type MediaPatch = components['schemas']['MediaPatch']
export type StreamRequest = components['schemas']['StreamRequest']
export type EncodeResponse = components['schemas']['EncodeResponse']

/** A mano y no generado: el script `types` sigue apuntando a FastAPI (TODO.md #13). */
export interface EncodingSettings {
  live_codec: string
  archive_codec: string
  live_codecs: string[]
  archive_codecs: string[]
  default_live_codec: string
  default_archive_codec: string
}

/** A mano, como `EncodingSettings`. Bytes bajo Cache:Dir y el tope. `used` puede pasar a `max`. */
export interface CacheUsage {
  used_bytes: number
  max_bytes: number
}

/** `interrupted` solo aparece con el asset fuera de memoria; `idle`, solo en el video. */
export type ArtifactState = 'idle' | 'pending' | 'building' | 'ready' | 'failed' | 'interrupted'

/** A mano, como `EncodingSettings`. `index` es null en el video. */
export interface ArtifactStatus {
  index: number | null
  state: ArtifactState
  /** Entre 0 y 1. Null en subtitulos hasta que terminan: no reportan avance. */
  progress: number | null
  /** En `building` pero esperando un slot de FFmpeg. */
  queued: boolean
  error: string | null
}

/** A mano, como `EncodingSettings`. Las pistas ignoradas no vienen. */
export interface MediaArtifacts {
  id_media: string
  asset_id: string | null
  pinned: boolean
  video_codec: string | null
  video: ArtifactStatus
  audio: ArtifactStatus[]
  subtitles: ArtifactStatus[]
}

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
  path_traversal: 'La ruta no es válida.',
  unsupported_extension: 'Formato no soportado. Solo .mp4 y .mkv.',
  file_not_found: 'No existe ese archivo.',
  outside_media_root: 'El archivo está fuera del directorio permitido.',
  too_many_jobs: 'El servidor está procesando otros videos. Reintentar en unos segundos.',
  storage_limit: 'El caché está lleno y no hay nada que liberar.',
  session_not_found: 'La sesión expiró o no existe.',
  asset_not_found: 'El video ya no está disponible.',
  playlist_not_ready: 'Todavía se está generando.',
  track_not_found: 'Esa pista no existe.',
  media_not_found: 'Esa ficha ya no existe.',
  media_already_exists: 'Ese archivo ya está en el catálogo.',
  asset_already_encoded: 'Ya hay una versión generada. Hay que borrarla del caché para volver a codificar.',
  invalid_media: 'El archivo no es un video que se pueda leer.',
  not_implemented: 'Todavía no está implementado.',
  media_root_not_configured: 'El servidor arrancó sin SM_MEDIA_ROOT: el catálogo no está disponible.',
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

async function send<T>(method: string, url: string, body: unknown): Promise<T> {
  const resp = await fetch(url, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  return unwrap<T>(resp)
}

export function post<T>(url: string, body: unknown): Promise<T> {
  return send<T>('POST', url, body)
}

export function patch<T>(url: string, body: unknown): Promise<T> {
  return send<T>('PATCH', url, body)
}

export function put<T>(url: string, body: unknown): Promise<T> {
  return send<T>('PUT', url, body)
}

/** Abre un stream. El backend pide exactamente uno de los dos campos: mandar
 *  los dos, o ninguno, es un 422. */
function openStream(body: StreamRequest): Promise<StreamResponse> {
  return post<StreamResponse>('/api/v1/stream', body)
}

/** Por ruta absoluta: el modo `?file` del player y el ingreso manual. */
export function openStreamByPath(filePath: string): Promise<StreamResponse> {
  return openStream({ file_path: filePath })
}

/**
 * Por ficha del catalogo: el modo `?media`, que es el que usa la galeria.
 *
 * La ficha guarda la ruta relativa a MEDIA_ROOT y el cliente no conoce el punto
 * de montaje del servidor; la absoluta la resuelve el backend. Armarla aca seria
 * hardcodear /media y acoplar el frontend al compose.
 */
export function openStreamByMedia(idMedia: string): Promise<StreamResponse> {
  return openStream({ id_media: idMedia })
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

function mediaParams(query: MediaQuery): URLSearchParams {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== '') params.set(key, String(value))
  }
  return params
}

export function listMedia(
  query: MediaQuery = {},
  signal?: AbortSignal,
): Promise<MediaListResponse> {
  return get<MediaListResponse>(`/api/v1/media?${mediaParams(query)}`, signal)
}

/** A mano, como `EncodingSettings`. Una pagina con la ficha completa en cada fila. */
export interface AdminMediaListResponse {
  items: MediaResponse[]
  total: number
  limit: number
  offset: number
}

/** Los mismos filtros que `listMedia`, con los datos tecnicos de cada ficha: la Biblioteca. */
export function listAdminMedia(
  query: MediaQuery = {},
  signal?: AbortSignal,
): Promise<AdminMediaListResponse> {
  return get<AdminMediaListResponse>(`/api/v1/admin/media?${mediaParams(query)}`, signal)
}

export function readMedia(
  idMedia: string,
  signal?: AbortSignal,
): Promise<MediaResponse> {
  return get<MediaResponse>(`/api/v1/media/${encodeURIComponent(idMedia)}`, signal)
}

/** Alta de una ficha. La ruta va relativa a MEDIA_ROOT ("films/Dune.mkv"),
 *  al reves que `POST /stream`, que la quiere absoluta. */
export function createMedia(filePath: string): Promise<MediaResponse> {
  return post<MediaResponse>('/api/v1/media', { file_path: filePath })
}

/**
 * Edita los campos editoriales. Devuelve la ficha completa, asi que el que
 * llama repinta con lo que respondio el servidor y no con lo que mando.
 *
 * Es un PATCH: lo que no viaja no se toca, y un campo en `null` se vacia. Los
 * derivados del archivo no se pueden mandar — `MediaPatch` los rechaza con un
 * 422 antes de llegar a la base.
 */
export function updateMedia(
  idMedia: string,
  fields: MediaPatch,
): Promise<MediaResponse> {
  return patch<MediaResponse>(
    `/api/v1/media/${encodeURIComponent(idMedia)}`,
    fields,
  )
}

/**
 * Arranca la codificacion de biblioteca y vuelve enseguida: el trabajo
 * puede tardar horas y el avance se sigue con `readEncode`.
 *
 * Solo se acepta con el video sin despachar (`idle` o `pending`); en
 * `building`, `ready` o `failed` es un 409 (`asset_already_encoded`). No se
 * reintenta ni se pisa: recodificar es borrar el output del cache a mano.
 */
export function startEncode(idMedia: string): Promise<EncodeResponse> {
  // Sin cuerpo: el id va en la URL y el perfil lo decide el servidor.
  // `JSON.stringify(undefined)` es `undefined`, o sea que no se manda body.
  return post<EncodeResponse>(
    `/api/v1/media/${encodeURIComponent(idMedia)}/encode`,
    undefined,
  )
}

export function readEncode(
  idMedia: string,
  signal?: AbortSignal,
): Promise<EncodeResponse> {
  return get<EncodeResponse>(
    `/api/v1/media/${encodeURIComponent(idMedia)}/encode`,
    signal,
  )
}

/** Solo lee: no abre el asset ni lanza FFmpeg. Se puede repetir sin efectos. */
export function readMediaArtifacts(
  idMedia: string,
  signal?: AbortSignal,
): Promise<MediaArtifacts> {
  return get<MediaArtifacts>(
    `/api/v1/admin/media/${encodeURIComponent(idMedia)}/artifacts`,
    signal,
  )
}

/** Recorre el disco en cada pedido: con el cache lleno puede tardar. */
export function readCacheUsage(signal?: AbortSignal): Promise<CacheUsage> {
  return get<CacheUsage>('/api/v1/admin/cache', signal)
}

export function readEncodingSettings(signal?: AbortSignal): Promise<EncodingSettings> {
  return get<EncodingSettings>('/api/v1/settings/encoding', signal)
}

/** Aplica a lo que todavia no se genero: no toca assets ni cancela builds. */
export function updateEncodingSettings(liveCodec: string, archiveCodec: string): Promise<EncodingSettings> {
  return put<EncodingSettings>('/api/v1/settings/encoding', { live_codec: liveCodec, archive_codec: archiveCodec })
}
