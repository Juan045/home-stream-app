import { WindowIcon } from './icons'

interface Props {
  title: string
  fullscreen: boolean
  visible: boolean
  onToggleFullscreen: () => void
}

/**
 * Barra de titulo de la ventana. En pantalla completa se vuelve translucida y
 * flota sobre el video; el boton pasa a ser el de restaurar.
 */
export function TitleBar({ title, fullscreen, visible, onToggleFullscreen }: Props) {
  return (
    <div className="titlebar" data-chrome data-visible={visible}>
      <span className="name">
        {title ? `${title} · Homeflix Classic` : 'Homeflix Classic'}
      </span>
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
    </div>
  )
}
