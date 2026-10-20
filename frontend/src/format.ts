import type { SubtitleTrack } from './api/client'

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
 * Duracion como la escribe el catalogo: "1 h 52 min", "58 min".
 *
 * Es otra cosa que formatTime, que es un timecode y necesita los segundos.
 * Aca los segundos son ruido: nadie elige que mirar por el segundo 07.
 */
export function formatRuntime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds <= 0) return '—'
  const total = Math.round(seconds / 60)
  const h = Math.floor(total / 60)
  const m = total % 60
  return h > 0 ? `${h} h ${String(m).padStart(2, '0')} min` : `${m} min`
}

/**
 * El atributo CHANNELS del master es un conteo ("6"), no una etiqueta: se
 * muestra como se habla de el ("5.1", "Estéreo").
 */
export function channelLabel(channels?: string | null): string | null {
  if (!channels) return null
  const count = Number.parseInt(channels, 10)
  if (!Number.isFinite(count) || count <= 0) return null
  if (count === 1) return 'Mono'
  if (count === 2) return 'Estéreo'
  if (count === 6) return '5.1'
  if (count === 8) return '7.1'
  return `${count} canales`
}

// Las pistas sin title se nombran por el idioma (ISO 639-2 de ffprobe).
const LANG_NAMES: Record<string, string> = {
  spa: 'Español',
  eng: 'English',
  por: 'Português',
  fre: 'Français',
  jpn: '日本語',
}

/** Lo que no esta en la tabla va en mayusculas: "ITA". */
export function subtitleLabel(track: SubtitleTrack): string {
  return (
    track.title ||
    LANG_NAMES[track.language] ||
    track.language.toUpperCase() ||
    `Pista ${track.index + 1}`
  )
}

/** El delay de subtitulos se escribe con signo: +0.4 s, −1.2 s. */
export function formatDelay(seconds: number): string {
  const value = Math.round(seconds * 10) / 10
  const sign = value < 0 ? '−' : '+'
  return `${sign}${Math.abs(value).toFixed(1)} s`
}

/** Lo que se escribe donde el backend todavia no tiene el dato. */
export const MISSING = 'Sin cargar todavía'

export const KINDS: Record<string, string> = {
  film: 'Película',
  series: 'Serie',
  documentary: 'Documental',
}
