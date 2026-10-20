/** Lo que comparten el alta (/new/) y la edicion (/edit/) que no es marcado. */
import { ApiError, errorMessage, type MediaResponse } from '../../api/client'
import { channelLabel } from '../../format'

/** Un error del servidor ya traducido, con el slug para mostrarlo como codigo. */
export interface Failure {
  msg: string
  slug: string | null
}

// ffprobe da ISO 639-2 y nombres de codec en minuscula; el admin los escribe enteros.
const LANG: Record<string, string> = { spa: 'Español', eng: 'Inglés', jpn: 'Japonés', fre: 'Francés', por: 'Portugués', ger: 'Alemán', ita: 'Italiano' }
const CODEC: Record<string, string> = { hevc: 'HEVC', h264: 'H.264', av1: 'AV1', eac3: 'E-AC3', ac3: 'AC3', aac: 'AAC', dts: 'DTS', truehd: 'TrueHD', subrip: 'SRT', hdmv_pgs_subtitle: 'PGS', ass: 'ASS' }

export const lang = (code: string) => LANG[code] ?? (code || '??').toUpperCase()
export const codec = (name: string) => CODEC[name] ?? name.toUpperCase()
export const channels = (count: number) => channelLabel(String(count))
export const videoLine = (media: MediaResponse) =>
  media.width && media.height ? `${codec(media.video_codec)} · ${media.width}×${media.height}` : codec(media.video_codec)

/** Sin respuesta del servidor (`fetch` rechaza) no hay slug que mostrar. */
export function toFailure(err: unknown): Failure {
  return err instanceof ApiError
    ? { msg: failure(err), slug: err.slug }
    : { msg: errorMessage('unknown'), slug: null }
}

/**
 * `invalid_path` significa lo contrario en los dos endpoints que lo usan: el
 * player manda la ruta absoluta y el alta la relativa. El mensaje compartido
 * habla del caso del player, asi que aca se traduce.
 *
 * El 422 no tiene slug: FastAPI contesta los errores de validacion con su
 * propio formato, asi que sin este caso saldria el mensaje de "no se pudo
 * conectar", que manda a buscar el problema al lugar equivocado.
 */
function failure(err: ApiError): string {
  if (err.slug === 'invalid_path') {
    return 'La ruta tiene que ser relativa a MEDIA_ROOT.'
  }
  // Unico `invalid_request` del PATCH: remux sobre un archivo que no es
  // H.264. La UI ya lo impide, pero la ficha pudo cambiar del lado del server.
  if (err.slug === 'invalid_request') {
    return 'Solo el video H.264 se reempaqueta sin recodificar: este archivo hay que transcodificarlo.'
  }
  if (err.status === 422) {
    return 'Hay un campo con un valor que el servidor no acepta.'
  }
  return errorMessage(err.slug)
}
