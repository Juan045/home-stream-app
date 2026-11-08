/** Lo que usan varias piezas de la galeria y no es marcado. */
import type { MediaResponse } from '../../api/client'
import { channelLabel } from '../../format'

// ffprobe da ISO 639-2; la galeria usa dos letras. Lo que no esta, en mayusculas.
const LANG: Record<string, string> = { spa: 'ES', eng: 'EN', jpn: 'JA', fre: 'FR', fra: 'FR', por: 'PT', ita: 'IT', ger: 'DE', deu: 'DE', kor: 'KO', rus: 'RU' }
const CODEC: Record<string, string> = { hevc: 'HEVC', h264: 'H.264', av1: 'AV1', vp9: 'VP9' }

export const lang = (code: string) => LANG[code] ?? (code || '??').toUpperCase()
export const codec = (name: string) => CODEC[name] ?? name.toUpperCase()
const resolution = (h: number) => (h >= 1500 ? '4K' : h >= 790 ? '1080p' : h >= 530 ? '720p' : `${h}p`)

/** Los idiomas de las pistas que se reproducen, sin repetir: "EN · ES". */
export const langs = (tracks: { language: string; ignore: boolean }[]) =>
  [...new Set(tracks.filter((t) => !t.ignore).map((t) => lang(t.language)))].join(' · ')

/** Resolucion, codec y canales de la mejor pista: ["1080p", "HEVC", "5.1"]. */
export function quality(media: MediaResponse): string[] {
  const best = Math.max(...media.audio_tracks.map((t) => t.channels))
  return [
    media.height ? resolution(media.height) : null,
    codec(media.video_codec),
    media.audio_tracks.length ? channelLabel(String(best)) : null,
  ].filter((q) => q !== null)
}

export const playerUrl = (id: string) => `/player/?media=${encodeURIComponent(id)}`

export const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)').matches
