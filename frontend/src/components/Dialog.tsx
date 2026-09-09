import type { ReactNode } from 'react'

interface Props {
  title: string
  children: ReactNode
  /** Sin handler no se dibuja la ✕: un dialogo de progreso no se puede cerrar. */
  onClose?: () => void
  wide?: boolean
}

/** Marco de dialogo modal: barra de titulo terracota y cuerpo con bisel. */
export function Dialog({ title, children, onClose, wide = false }: Props) {
  return (
    <div className="modal" role="presentation">
      <div
        className={wide ? 'dialog wide' : 'dialog'}
        role="dialog"
        aria-modal="true"
        aria-label={title}
      >
        <div className="bar">
          <span>{title}</span>
          {onClose && (
            <button
              className="title-btn"
              type="button"
              onClick={onClose}
              aria-label="Cerrar"
            >
              ✕
            </button>
          )}
        </div>
        <div className="body">{children}</div>
      </div>
    </div>
  )
}
