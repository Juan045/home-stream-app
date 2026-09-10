import type { AudioTrack, MediaResponse } from '../../api/client'
import { PlayIcon } from '../../components/icons'
import { channelLabel, formatRuntime } from '../../format'
import type { Detail } from './hooks/useMediaDetail'

/** Lo que se escribe donde el backend todavia no tiene el dato. */
const MISSING = 'no available yet'

const KINDS: Record<string, string> = {
  film: 'Film',
  series: 'Series',
  documentary: 'Documentary',
}

function audioLabel(track: AudioTrack): string {
  const parts = [track.language.toUpperCase(), track.codec.toUpperCase()]
  const channels = channelLabel(String(track.channels))
  if (channels) parts.push(channels)
  return parts.join(' ')
}

/**
 * Los campos de la ficha, en el orden en que se leen.
 *
 * Los que el backend no llena hoy — ano, generos, sinopsis — no se ocultan:
 * se muestran con su etiqueta y el hueco a la vista. Que la ficha este
 * incompleta es informacion, y esconderla haria parecer que el catalogo sabe
 * menos de lo que va a saber.
 */
function fields(media: MediaResponse): [string, string][] {
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

/** El panel de la derecha. Ocupa su ancho siempre, con o sin seleccion. */
export function DetailPanel({ media, loading, error }: Detail) {
  if (loading || error !== null || media === null) {
    const note = loading
      ? 'Cargando la ficha…'
      : (error ?? 'Elegi un titulo para ver su ficha.')
    return (
      <aside className="side" aria-label="Detalle del titulo" aria-busy={loading}>
        <p className="side-empty">{note}</p>
      </aside>
    )
  }

  return (
    <aside className="side" aria-label="Detalle del titulo">
      <div className="poster big">
        <span>Image not Available</span>
      </div>

      <h2 className="name">{media.title}</h2>

      <dl className="facts">
        {fields(media).map(([label, value]) => (
          <div className="fact" key={label}>
            <dt>{label}</dt>
            <dd data-missing={value === MISSING}>{value}</dd>
          </div>
        ))}
      </dl>

      <p className="synopsis" data-missing={media.synopsis === null}>
        {media.synopsis ?? MISSING}
      </p>

      <div className="actions">
        {/* Sin conectar a proposito. `POST /stream` pide una ruta absoluta y la
            ficha guarda la relativa a MEDIA_ROOT, que el SPA no conoce: armarla
            aca seria hardcodear /media y acoplar el frontend al compose. */}
        <button className="btn primary" type="button" title="no available yet">
          <PlayIcon /> Reproducir
        </button>
        <button className="btn" type="button" title="no available yet">
          Ver ficha
        </button>
      </div>
    </aside>
  )
}
