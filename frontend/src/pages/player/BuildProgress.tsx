import { Dialog } from '../../components/Dialog'

interface Props {
  /** Nombre del medio, para que el dialogo diga sobre que esta trabajando. */
  title: string
  label: string
  detail: string
  /** 0 a 1, avance del build de video. null cuando solo se esta rebufereando. */
  progress: number | null
}

const SLOTS = 20

/**
 * El dialogo de espera. No lleva ✕ ni Cancelar: no hay endpoint para abortar
 * un build, y un boton que no hace nada miente sobre lo que el servidor puede.
 */
export function BuildProgress({ title, label, detail, progress }: Props) {
  const filled =
    progress === null ? 4 : Math.max(1, Math.round(progress * SLOTS))

  return (
    <Dialog title={`${label}…`}>
      <p className="text">
        {label}: <b>{title || 'el video'}</b>
        <br />
        {detail}
      </p>
      <div
        className="blocks"
        data-indeterminate={progress === null}
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={progress === null ? undefined : Math.round(progress * 100)}
        aria-label={label}
      >
        {Array.from({ length: filled }, (_, i) => (
          <i key={i} />
        ))}
      </div>
    </Dialog>
  )
}
