import { PosterCard } from './PosterCard'
import type { Section } from './mock'

interface Props {
  section: Section
  selected: string | null
  onSelect: (id: string) => void
}

/** Una fila del catalogo: el encabezado de la seccion y sus tarjetas. */
export function PosterRail({ section, selected, onSelect }: Props) {
  const { items, total } = section.page

  return (
    <section className="rail" aria-label={section.title}>
      <div className="rail-head">
        <span className="title">{section.title}</span>
        <div className="right">
          <span className="count">
            {total} {total === 1 ? 'title' : 'titles'}
          </span>
          {/* Las flechas son parte del marco. Quedan inertes hasta que una
              seccion tenga mas titulos de los que entran en la fila. */}
          <div className="arrows">
            <button className="arrow" type="button" aria-label="Anterior">
              <i className="left" />
            </button>
            <button className="arrow" type="button" aria-label="Siguiente">
              <i className="right" />
            </button>
          </div>
        </div>
      </div>

      <div className="rail-row">
        {items.length === 0 ? (
          <p className="empty">Sin titulos en esta seccion.</p>
        ) : (
          items.map((media) => (
            <PosterCard
              key={media.id_media}
              media={media}
              selected={media.id_media === selected}
              onSelect={() => onSelect(media.id_media)}
            />
          ))
        )}
      </div>
    </section>
  )
}
