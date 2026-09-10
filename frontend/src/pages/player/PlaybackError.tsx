import { Dialog } from '../../components/Dialog'

interface Props {
  title: string
  message: string
  /** Slug de la API o detalle de hls.js: lo unico estable para reportar. */
  code?: string | null
  onRetry?: () => void
}

export function PlaybackError({ title, message, code, onRetry }: Props) {
  return (
    <Dialog title={title}>
      <div className="notice" role="alert">
        <div className="sign" aria-hidden="true">
          !
        </div>
        <div>
          <p className="text">{message}</p>
          {code && <span className="code">{code}</span>}
          <div className="buttons">
            {onRetry && (
              <button className="dlg-btn default" type="button" onClick={onRetry}>
                Reintentar
              </button>
            )}
            <button
              className="dlg-btn"
              type="button"
              onClick={() => location.reload()}
            >
              Recargar
            </button>
          </div>
        </div>
      </div>
    </Dialog>
  )
}
