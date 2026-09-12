import { PlayIcon } from '../../components/icons'
import { MISSING, mediaFacts } from '../../format'
import type { Detail } from './hooks/useMediaDetail'

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
        {mediaFacts(media).map(([label, value]) => (
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
