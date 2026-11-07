/** Una fila del tablero: indice, punto de estado, que es y avance. El error de FFmpeg va debajo. */
import type { ArtifactStatus } from '../../api/client'
import { Icon } from '../../components/Icon'
import { NAMES, look } from './artifacts'

export function ArtifactRow(props: { idx: string; name: string; meta: string; artifact: ArtifactStatus | null }) {
  const { idx, name, meta, artifact } = props

  // Ignorada: sigue en el archivo pero no es artefacto. Sin punto ni barra.
  if (artifact === null) {
    return (
      <li className="art is-ignored" title="Ignorada">
        <span className="idx">{idx}</span>
        <div className="what"><b>{name}</b><small>{meta}</small></div>
        <span className="meter">No se genera</span>
      </li>
    )
  }

  const shown = look(artifact)
  // Sin porcentaje: en cola todavia no arranco, y un subtitulo no reporta avance hasta terminar.
  const percent = shown === 'queued' || artifact.progress === null ? null : Math.round(artifact.progress * 100)
  const moving = shown === 'queued' || (shown === 'building' && percent === null)
  const label = (shown === 'building' || shown === 'ready') && percent !== null ? `${percent}%` : '—'

  return (
    <li className={`art is-${shown}`}>
      <span className="idx">{idx}</span>
      <span className="dot" role="img" aria-label={NAMES[shown]} title={NAMES[shown]} />
      <div className="what"><b>{name}</b><small>{meta}</small></div>
      <div className="meter">
        <div
          className={moving ? 'bar moving' : 'bar'}
          role="progressbar"
          aria-label={name}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={percent ?? undefined}
        >
          <i style={{ width: `${moving ? 0 : percent ?? 0}%` }} />
        </div>
        <span className="pct">{label}</span>
      </div>
      {/* El stderr completo: es un panel de admin y es lo unico que explica el fallo. */}
      {artifact.error && (
        <details className="st-err">
          <summary><Icon name="chev-right" /><code>{artifact.error}</code></summary>
        </details>
      )}
    </li>
  )
}
