import type { AudioTrack, MediaResponse, SubtitleTrack } from './api/client'

/** Timecode hh:mm:ss, o mm:ss cuando dura menos de una hora. */
export function formatTime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return '0:00'
  const total = Math.floor(seconds)
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  const mm = h > 0 ? String(m).padStart(2, '0') : String(m)
  return h > 0
    ? `${h}:${mm}:${String(s).padStart(2, '0')}`
    : `${mm}:${String(s).padStart(2, '0')}`
}

/**
 * Duracion como la escribe el catalogo: "1 h 52", "58 min".
 *
 * Es otra cosa que formatTime, que es un timecode y necesita los segundos.
 * Aca los segundos son ruido: nadie elige que mirar por el segundo 07.
 */
export function formatRuntime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds <= 0) return '—'
  const total = Math.round(seconds / 60)
  const h = Math.floor(total / 60)
  const m = total % 60
  return h > 0 ? `${h} h ${String(m).padStart(2, '0')}` : `${m} min`
}

/**
 * El atributo CHANNELS del master es un conteo ("6"), no una etiqueta. La
 * barra de estado muestra la forma en que se habla de el.
 */
export function channelLabel(channels?: string | null): string | null {
  if (!channels) return null
  const count = Number.parseInt(channels, 10)
  if (!Number.isFinite(count) || count <= 0) return null
  if (count === 1) return 'Mono'
  if (count === 2) return 'Estereo'
  if (count === 6) return '5.1'
  if (count === 8) return '7.1'
  return `${count} canales`
}

export function subtitleLabel(track: SubtitleTrack): string {
  return track.title || track.language || `Pista ${track.index + 1}`
}

/** El delay de subtitulos se escribe con signo: +0.4 s, -1.2 s. */
export function formatDelay(seconds: number): string {
  const value = Math.round(seconds * 10) / 10
  const sign = value < 0 ? '-' : '+'
  return `${sign}${Math.abs(value).toFixed(1)} s`
}

/** Lo que se escribe donde el backend todavia no tiene el dato. */
export const MISSING = 'no available yet'

const KINDS: Record<string, string> = {
  film: 'Film',
  series: 'Series',
  documentary: 'Documentary',
}

export function audioLabel(track: AudioTrack): string {
  const parts = [track.language.toUpperCase(), track.codec.toUpperCase()]
  const channels = channelLabel(String(track.channels))
  if (channels) parts.push(channels)
  return parts.join(' ')
}

/**
 * Los campos de una ficha, en el orden en que se leen.
 *
 * Los que el backend no llena hoy — ano, generos, sinopsis — no se ocultan: se
 * muestran con su etiqueta y el hueco a la vista. Que la ficha este incompleta
 * es informacion, y esconderla haria parecer que el catalogo sabe menos de lo
 * que va a saber.
 *
 * Lo comparten el panel de detalle de la galeria y la confirmacion del alta:
 * es la misma ficha leida en dos momentos distintos.
 */
export function mediaFacts(media: MediaResponse): [string, string][] {
  const resolution =
    media.width && media.height ? `${media.width}×${media.height}` : MISSING

  const audio =
    media.audio_tracks.length > 0
      ? media.audio_tracks.map(audioLabel).join(', ')
      : MISSING

  const subtitles =
    media.subtitle_tracks.length > 0
      ? media.subtitle_tracks
          .map((track) => track.language.toUpperCase() || '??')
          .join(' · ')
      : MISSING

  return [
    ['Kind', KINDS[media.kind] ?? media.kind],
    ['Year', media.year === null ? MISSING : String(media.year)],
    ['Runtime', formatRuntime(media.duration)],
    ['Video', `${media.video_codec} · ${resolution}`],
    ['Audio', audio],
    ['Subtitles', subtitles],
    ['Genres', media.genres.length > 0 ? media.genres.join(', ') : MISSING],
    ['File', media.file_name],
  ]
}
