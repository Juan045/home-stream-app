/** Lo que ocupa el cache contra su tope, arriba de la tabla. Solo mide al tocar el boton. */
import { Icon } from '../../components/Icon'
import { formatBytes, formatClock } from '../../format'
import { ServerAlert } from './admin'
import { useCacheUsage } from './hooks/useCacheUsage'

export function CacheUsageCard() {
  const { measurement, failure, measuring, measure } = useCacheUsage()
  const usage = measurement?.usage ?? null
  const ratio = usage === null ? 0 : usage.used_bytes / usage.max_bytes
  const percent = Math.round(ratio * 100)
  // Puede pasar el tope: el GC solo corre al abrir un titulo nuevo y los builds siguen escribiendo.
  const over = ratio > 1

  let text = 'Sin medir. El servidor recorre el disco, así que con el cache lleno puede tardar.'
  if (measuring && usage === null) text = 'Recorriendo el cache…'
  if (usage !== null && measurement !== null) {
    text = `${formatBytes(usage.used_bytes)} de ${formatBytes(usage.max_bytes)} · ${percent} % · medido a las ${formatClock(measurement.at)}`
    if (over) text += '. Pasa el tope: se libera espacio al abrir un título nuevo.'
  }

  return (
    <section className="card cache" aria-labelledby="cache-title">
      {measuring && <div className="progress" aria-hidden="true" />}
      <span className="ico"><Icon name="box" /></span>
      <div className="cache-txt">
        <h2 id="cache-title">Cache</h2>
        <p role="status">{text}</p>
      </div>
      <button className="btn btn-outlined" type="button" onClick={measure} disabled={measuring}>
        <Icon name="cycle" />{measuring ? 'Midiendo…' : usage === null ? 'Medir' : 'Volver a medir'}
      </button>
      {usage !== null && (
        <div
          className={over ? 'cache-bar over' : 'cache-bar'}
          role="progressbar"
          aria-label="Uso del cache"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.min(percent, 100)}
          aria-valuetext={`${percent} %`}
        >
          <i style={{ width: `${Math.min(ratio, 1) * 100}%` }} />
        </div>
      )}
      {failure !== null && <ServerAlert failure={failure} />}
    </section>
  )
}
