/**
 * Datos de ejemplo de la galeria, mientras la vista no habla con el backend.
 *
 * La forma es la de `MediaListResponse` y `MediaResponse` (app/models/
 * schemas.py) y los valores son los que produce el ABM hoy: el titulo es el
 * nombre del archivo sin extension, que es lo que pone `Media.from_source`, y
 * los campos editoriales salen vacios porque el alta no los llena. Lo unico
 * que aparece cargado es `in_list`, que es lo que se marca desde la ficha.
 *
 * No hay poster, no hay sinopsis y no hay progreso de reproduccion porque el
 * backend no los tiene. La vista se ve vacia a proposito: es lo que hay.
 *
 * Los tipos estan declarados aca y no en `api/types.ts` a proposito. Los de la
 * API se generan del /openapi.json y ese archivo todavia no conoce el ABM
 * (solo tiene /stream, /sessions, /heartbeat y las playlists). Al conectar:
 * levantar el backend con SM_DEBUG=1, correr `npm run types` e importar los
 * generados; estas interfaces se van con este archivo.
 */

export type MediaKind = 'film' | 'series' | 'documentary'

export interface AudioTrack {
  index: number
  codec: string
  channels: number
  language: string
  title: string
}

export interface SubtitleTrack {
  index: number
  codec: string
  language: string
  title: string
  url: string | null
}

/** Una fila de la grilla. Es lo unico que devuelve `GET /media`. */
export interface MediaListItem {
  id_media: string
  title: string
  kind: MediaKind
  year: number | null
  duration: number
}

export interface MediaListResponse {
  items: MediaListItem[]
  total: number
  limit: number
  offset: number
}

/** La ficha entera, que es lo que devuelve `GET /media/{id_media}`. */
export interface MediaDetail extends MediaListItem {
  file_path: string
  file_name: string
  asset_id: string | null
  synopsis: string | null
  genres: string[]
  notes: string | null
  in_list: boolean
  video_codec: string
  width: number | null
  height: number | null
  strategy: string
  audio_tracks: AudioTrack[]
  subtitle_tracks: SubtitleTrack[]
  created_at: string
  updated_at: string
}

const STEREO: AudioTrack[] = [
  { index: 0, codec: 'aac', channels: 2, language: 'eng', title: '' },
]

const SURROUND_ITA_ENG: AudioTrack[] = [
  { index: 0, codec: 'ac3', channels: 6, language: 'ita', title: '' },
  { index: 1, codec: 'ac3', channels: 6, language: 'eng', title: '' },
]

const SUBS_ES: SubtitleTrack[] = [
  { index: 0, codec: 'subrip', language: 'spa', title: '', url: null },
]

const SUBS_ES_EN: SubtitleTrack[] = [
  { index: 0, codec: 'subrip', language: 'spa', title: '', url: null },
  { index: 1, codec: 'subrip', language: 'eng', title: '', url: null },
]

/**
 * El catalogo completo. Cada entrada es lo que devolveria el detalle; las
 * secciones de abajo son las paginas que devolveria el listado.
 */
const CATALOG: MediaDetail[] = [
  entry({
    id: '3f2a9c1e4b7d4f8a9c2e5b1d7a3f6c80',
    path: "films/Schindler's List (1993) BDrip 1080p ITA-ENG x264 -WGZ.mkv",
    kind: 'film',
    duration: 11712.736,
    in_list: true,
    audio: SURROUND_ITA_ENG,
    subtitles: SUBS_ES_EN,
  }),
  entry({
    id: '9c74b1e0a5d24f13b8e6c0a7d215f4bb',
    path: 'films/Cold Harvest (2023) BDrip 1080p ITA-ENG x264 -WGZ.mkv',
    kind: 'film',
    duration: 6727.4,
    in_list: true,
    audio: SURROUND_ITA_ENG,
    subtitles: SUBS_ES,
  }),
  entry({
    id: 'd18f6a3c7b924e05a1c3f8d20e64b7a9',
    path: 'films/Paper.Lanterns.2021.1080p.BluRay.x265-RARBG.mkv',
    kind: 'film',
    duration: 5893.12,
    codec: 'hevc',
    strategy: 'transcode',
    audio: STEREO,
    subtitles: [],
  }),
  entry({
    id: '4a0e2d5b8c1f47a396b0e7d3c25a81f6',
    path: 'films/Blue Hour Drive (2019) 1080p WEB-DL DDP5.1 H.264.mp4',
    kind: 'film',
    duration: 7455.0,
    audio: [{ index: 0, codec: 'eac3', channels: 6, language: 'eng', title: '' }],
    subtitles: SUBS_ES,
  }),
  entry({
    id: 'b62c9f04a7d1436e85f2c0b93d17e6a4',
    path: 'films/Ravine.2020.720p.WEBRip.x264.mkv',
    kind: 'film',
    duration: 6981.6,
    width: 1280,
    height: 720,
    audio: STEREO,
    subtitles: [],
  }),
  entry({
    id: '7d3b0c8e5f1a42d69e07b4a2c8f501d3',
    path: 'series/Northern Lines/Northern.Lines.S02E04.1080p.WEB-DL.x264.mkv',
    kind: 'series',
    duration: 3485.0,
    in_list: true,
    audio: STEREO,
    subtitles: SUBS_ES_EN,
  }),
  entry({
    id: 'e05a1c74b93f428d6072a5e8c1b3f9d0',
    path: 'series/Harbour Nine/Harbour Nine - S01E07 [1080p][AAC 2.0].mkv',
    kind: 'series',
    duration: 2892.32,
    audio: STEREO,
    subtitles: SUBS_ES,
  }),
  entry({
    id: 'c41d7e0b6a3f48291e5d47c0a2b6f381',
    path: 'series/Signal Hill/Signal.Hill.S01E01.1080p.HDTV.x264.mkv',
    kind: 'series',
    duration: 3204.88,
    audio: STEREO,
    subtitles: [],
  }),
  entry({
    id: 'a83f0d26c5b14e79802d6f1a3c94b5e7',
    path: 'docs/Deep Field (2022) 1080p Documentary x264.mkv',
    kind: 'documentary',
    duration: 4867.2,
    in_list: true,
    audio: STEREO,
    subtitles: SUBS_ES,
  }),
  entry({
    id: '5b9e4a1c0d72463f81a5c3e70b2d894f',
    path: 'docs/The.Understory.2021.1080p.WEB.h264.mp4',
    kind: 'documentary',
    duration: 3512.0,
    audio: STEREO,
    subtitles: [],
  }),
]

interface Seed {
  id: string
  path: string
  kind: MediaKind
  duration: number
  audio: AudioTrack[]
  subtitles: SubtitleTrack[]
  in_list?: boolean
  codec?: string
  strategy?: string
  width?: number
  height?: number
}

/** Arma la ficha como la armaria el alta: el titulo es el nombre del archivo. */
function entry(seed: Seed): MediaDetail {
  const name = seed.path.split('/').pop() ?? seed.path
  const stamp = '2026-09-08T21:14:32+00:00'

  return {
    id_media: seed.id,
    file_path: seed.path,
    file_name: name,
    asset_id: seed.id.slice(0, 16),
    title: name.replace(/\.[^.]+$/, ''),
    kind: seed.kind,
    year: null,
    synopsis: null,
    genres: [],
    notes: null,
    in_list: seed.in_list ?? false,
    duration: seed.duration,
    video_codec: seed.codec ?? 'h264',
    width: seed.width ?? 1920,
    height: seed.height ?? 1080,
    strategy: seed.strategy ?? 'remux',
    audio_tracks: seed.audio,
    subtitle_tracks: seed.subtitles,
    created_at: stamp,
    updated_at: stamp,
  }
}

function page(items: MediaDetail[]): MediaListResponse {
  return {
    items: items.map(({ id_media, title, kind, year, duration }) => ({
      id_media,
      title,
      kind,
      year,
      duration,
    })),
    total: items.length,
    limit: 10,
    offset: 0,
  }
}

export interface Section {
  title: string
  /** La query que la traeria. Queda a la vista para cuando haya que conectarla. */
  query: string
  page: MediaListResponse
}

/**
 * Una seccion es una llamada al listado con sus filtros, no un corte en el
 * cliente: por eso un titulo marcado aparece dos veces, en "My list" y en la
 * seccion de su tipo. Es lo que devolveria el servidor.
 *
 * Falta "Continue watching": necesita el progreso de reproduccion, y
 * `PUT /media/{id_media}/progress` todavia responde 501.
 */
export const SECTIONS: Section[] = [
  {
    title: 'My list',
    query: 'in_list=true',
    page: page(CATALOG.filter((media) => media.in_list)),
  },
  {
    title: 'Series',
    query: 'kind=series',
    page: page(CATALOG.filter((media) => media.kind === 'series')),
  },
  {
    title: 'Films',
    query: 'kind=film',
    page: page(CATALOG.filter((media) => media.kind === 'film')),
  },
  {
    title: 'Documentaries',
    query: 'kind=documentary',
    page: page(CATALOG.filter((media) => media.kind === 'documentary')),
  },
]

/** El "31 titles" del pie: el `total` de un listado sin filtros. */
export const LIBRARY_TOTAL = CATALOG.length

const BY_ID = new Map(CATALOG.map((media) => [media.id_media, media]))

/** Lo que traeria `GET /media/{id_media}` para el panel de la derecha. */
export function detailOf(id: string | null): MediaDetail | null {
  return id === null ? null : (BY_ID.get(id) ?? null)
}
