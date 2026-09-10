import { WindowIcon } from './icons'

interface Props {
  /** Se muestra tal cual. Quien llama arma el texto que quiere. */
  title: string
  /** Sin handler no hay boton: la galeria no tiene pantalla completa. */
  onToggleFullscreen?: () => void
  fullscreen?: boolean
  /** Solo cuenta en pantalla completa, donde el chrome se esconde solo. */
  visible?: boolean
}

/**
 * Barra de titulo de la ventana. En pantalla completa se vuelve translucida y
 * flota sobre el video; el boton pasa a ser el de restaurar.
 */
export function TitleBar({
  title,
  onToggleFullscreen,
  fullscreen = false,
  visible = true,
}: Props) {
  return (
    <div className="titlebar" data-chrome data-visible={visible}>
      <span className="name">{title}</span>
      {onToggleFullscreen && (
        <div className="box">
          <button
            className="title-btn"
            type="button"
            onClick={onToggleFullscreen}
            aria-label={fullscreen ? 'Restaurar ventana' : 'Pantalla completa'}
          >
            <WindowIcon restore={fullscreen} />
          </button>
        </div>
      )}
    </div>
  )
}
