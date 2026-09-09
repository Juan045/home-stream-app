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
