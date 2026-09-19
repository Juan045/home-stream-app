/**
 * Catalogo, artboard 1b de Homeflix Gallery: filas de posters por seccion con
 * el panel de detalle al costado.
 *
 * Consume `GET /media` (una llamada por seccion) y `GET /media/{id_media}`
 * para el panel. El resto de los controles queda dibujado pero inerte: no hay
 * endpoint detras. La reproduccion tampoco se dispara desde aca todavia.
 */
import { useEffect, useState } from 'react'
import { StatusBar } from '../../components/StatusBar'
import { TitleBar } from '../../components/TitleBar'
import { DetailPanel } from './DetailPanel'
import { LibraryToolbar, type Sort } from './LibraryToolbar'
import { PosterRail } from './PosterRail'
import { useEncode } from './hooks/useEncode'
import { useLibrary } from './hooks/useLibrary'
import { useMediaDetail } from './hooks/useMediaDetail'
import './gallery.css'

/** Lo que se tarda en dejar de tipear antes de salir a buscar. */
const DEBOUNCE_MS = 300

export default function GalleryPage() {
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState<Sort>('title')
  const [selected, setSelected] = useState<string | null>(null)

  // Sin esto sale una request por tecla. El input responde igual: lo que se
  // demora es la busqueda, no lo que se ve escrito.
  useEffect(() => {
    const timer = setTimeout(() => setQuery(search), DEBOUNCE_MS)
    return () => clearTimeout(timer)
  }, [search])

  const library = useLibrary(query, sort)
  const detail = useMediaDetail(selected)
  // Va aca y no adentro del panel porque el panel arranca con un return
  // temprano mientras la ficha carga, y un hook no puede vivir despues de eso.
  const encode = useEncode(selected)

  return (
    <div className="window library">
      <TitleBar title="Homeflix — My library" />

      <LibraryToolbar
        search={search}
        onSearch={setSearch}
        sort={sort}
        onSort={setSort}
      />

      <div className="shelves">
        <div className="rails" aria-busy={library.loading}>
          {library.error !== null ? (
            <p className="shelf-note">{library.error}</p>
          ) : library.loading ? (
            <p className="shelf-note">Cargando el catalogo…</p>
          ) : library.total === 0 ? (
            <p className="shelf-note">no media available</p>
          ) : (
            library.sections.map((section) => (
              <PosterRail
                key={section.title}
                section={section}
                selected={selected}
                onSelect={setSelected}
              />
            ))
          )}
        </div>

        <DetailPanel detail={detail} encode={encode} />
      </div>

      <StatusBar
        state={status(library.loading, library.error, library.total, query)}
        format={
          detail.media ? `Selected: ${detail.media.title}` : 'Sin seleccion'
        }
        duration="Posters"
      />
    </div>
  )
}

function status(
  loading: boolean,
  error: string | null,
  total: number,
  query: string,
): string {
  if (error !== null) return 'Error'
  if (loading) return 'Cargando…'
  const titles = `${total} ${total === 1 ? 'title' : 'titles'}`
  return query ? `${titles} · "${query}"` : titles
}
