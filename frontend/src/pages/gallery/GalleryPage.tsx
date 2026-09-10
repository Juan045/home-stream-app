/**
 * Catalogo, artboard 1b de Homeflix Gallery: filas de posters por seccion con
 * el panel de detalle al costado.
 *
 * Todavia no habla con el backend: los datos salen de mock.ts, con la forma
 * que devuelve `GET /media`. Lo unico que hace es la seleccion, que es estado
 * del cliente y no cambia cuando se conecte.
 */
import { useState } from 'react'
import { StatusBar } from '../../components/StatusBar'
import { TitleBar } from '../../components/TitleBar'
import { DetailPanel } from './DetailPanel'
import { LibraryToolbar } from './LibraryToolbar'
import { PosterRail } from './PosterRail'
import { LIBRARY_TOTAL, SECTIONS, detailOf } from './mock'
import './gallery.css'

export default function GalleryPage() {
  const [selected, setSelected] = useState<string | null>(null)
  const media = detailOf(selected)

  return (
    <div className="window library">
      <TitleBar title="Homeflix — My library" />

      <LibraryToolbar />

      <div className="shelves">
        <div className="rails">
          {SECTIONS.map((section) => (
            <PosterRail
              key={section.title}
              section={section}
              selected={selected}
              onSelect={setSelected}
            />
          ))}
        </div>

        <DetailPanel media={media} />
      </div>

      <StatusBar
        state={`${LIBRARY_TOTAL} titles`}
        format={media ? `Selected: ${media.title}` : 'Sin seleccion'}
        duration="Posters"
      />
    </div>
  )
}
