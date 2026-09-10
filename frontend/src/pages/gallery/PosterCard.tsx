import { formatRuntime } from '../../format'
import type { MediaKind, MediaListItem } from './mock'

interface Props {
  media: MediaListItem
  selected: boolean
  onSelect: () => void
}

const KINDS: Record<MediaKind, string> = {
  film: 'Film',
  series: 'Series',
  documentary: 'Doc',
}

/**
 * La linea de abajo del titulo. Con lo que hay en la ficha alcanza para el
 * tipo, el ano y la duracion; las temporadas y los episodios del mockup
 * necesitan columnas que la tabla no tiene.
 */
function subtitle(media: MediaListItem): string {
  const parts = [KINDS[media.kind]]
  if (media.year !== null) parts.push(String(media.year))
  parts.push(formatRuntime(media.duration))
  return parts.join(' · ')
}

export function PosterCard({ media, selected, onSelect }: Props) {
  return (
    <button
      className="card"
      type="button"
      aria-pressed={selected}
      data-on={selected}
      onClick={onSelect}
    >
      <div className="poster">
        <span>Image not Available</span>
      </div>
      <span className="name">{media.title}</span>
      <span className="meta">{subtitle(media)}</span>
    </button>
  )
}
