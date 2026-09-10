import { PlayIcon } from '../../components/icons'
import { channelLabel, formatRuntime } from '../../format'
import type { MediaDetail } from './mock'

interface Props {
  media: MediaDetail | null
}

const KINDS: Record<string, string> = {
  film: 'Film',
  series: 'Series',
  documentary: 'Documentary',
}

/**
 * La linea tecnica: solo lo que ffprobe dejo en la ficha. El mockup escribe
 * ademas el episodio y el genero, que hoy salen vacios de la base.
 */
function techLine(media: MediaDetail): string {
  const parts: string[] = [KINDS[media.kind] ?? media.kind]

  if (media.year !== null) parts.push(String(media.year))
  parts.push(formatRuntime(media.duration))
  if (media.width && media.height) parts.push(`${media.width}×${media.height}`)

  const channels = channelLabel(String(media.audio_tracks[0]?.channels ?? ''))
  if (channels) parts.push(channels)

  const langs = media.subtitle_tracks
    .map((track) => track.language.toUpperCase())
    .filter(Boolean)
  if (langs.length > 0) parts.push(langs.join('/'))

  return parts.join(' · ')
}

/** El panel de la derecha. Ocupa su ancho siempre, con o sin seleccion. */
export function DetailPanel({ media }: Props) {
  if (media === null) {
    return (
      <aside className="side" aria-label="Detalle del titulo">
        <p className="side-empty">Elegi un titulo para ver su ficha.</p>
      </aside>
    )
  }

  return (
    <aside className="side" aria-label="Detalle del titulo">
      <div className="poster big">
        <span>Image not Available</span>
      </div>

      <h2 className="name">{media.title}</h2>
      <p className="tech">{techLine(media)}</p>
      <p className="synopsis" data-empty={media.synopsis === null}>
        {media.synopsis ?? 'Sin sinopsis.'}
      </p>

      <div className="actions">
        <button className="btn primary" type="button">
          <PlayIcon /> Reproducir
        </button>
        <button className="btn" type="button">
          Ver ficha
        </button>
      </div>
    </aside>
  )
}
