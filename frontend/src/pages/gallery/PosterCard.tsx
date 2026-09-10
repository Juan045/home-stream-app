import type { MediaListItem } from '../../api/client'
import { formatRuntime } from '../../format'

interface Props {
  media: MediaListItem
  selected: boolean
  onSelect: () => void
}

const KINDS: Record<string, string> = {
  film: 'Film',
  series: 'Series',
  documentary: 'Doc',
}

/**
 * La linea de abajo del titulo. Lleva solo lo que el listado trae siempre; lo
 * que puede faltar — el ano — se omite en vez de ocupar lugar en 116px. La
 * ficha entera, con los huecos a la vista, esta en el panel de la derecha.
 */
function subtitle(media: MediaListItem): string {
  const parts = [KINDS[media.kind] ?? media.kind]
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
      {/* El backend no tiene artwork: no hay campo, no hay archivo, no hay
          endpoint. El rayado del artboard se queda con la leyenda cambiada. */}
      <div className="poster">
        <span>Image not Available</span>
      </div>
      <span className="name">{media.title}</span>
      <span className="meta">{subtitle(media)}</span>
    </button>
  )
}
