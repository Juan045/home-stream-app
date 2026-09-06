import { useRef, useState, type KeyboardEvent, type PointerEvent } from 'react'

interface Props {
  /** seek: la barra ancha del video. level: la corta del volumen. */
  kind: 'seek' | 'level'
  /** Posicion actual, 0 a 1. */
  value: number
  /** Fraccion ya generada en disco, solo para la barra de video. */
  built?: number
  /**
   * Con live, cada movimiento del puntero emite. Sin live se emite recien al
   * soltar: arrastrar el cabezal del video buscando en cada pixel encadena
   * seeks que hls.js no llega a servir.
   */
  live?: boolean
  label: string
  valueText?: string
  disabled?: boolean
  /** Salto de cada flecha del teclado, en fraccion de la barra. */
  step: number
  onChange: (ratio: number) => void
}

const clamp = (n: number) => Math.max(0, Math.min(n, 1))

export function Slider({
  kind,
  value,
  built,
  live = false,
  label,
  valueText,
  disabled = false,
  step,
  onChange,
}: Props) {
  const railRef = useRef<HTMLDivElement>(null)
  const dragging = useRef(false)
  const [preview, setPreview] = useState<number | null>(null)

  const shown = clamp(preview ?? value)

  const ratioAt = (clientX: number): number => {
    const rail = railRef.current
    if (!rail) return 0
    const box = rail.getBoundingClientRect()
    return clamp((clientX - box.left) / box.width)
  }

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    if (disabled) return
    event.currentTarget.setPointerCapture(event.pointerId)
    dragging.current = true
    const ratio = ratioAt(event.clientX)
    setPreview(ratio)
    if (live) onChange(ratio)
  }

  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    if (!dragging.current) return
    const ratio = ratioAt(event.clientX)
    setPreview(ratio)
    if (live) onChange(ratio)
  }

  const onPointerUp = (event: PointerEvent<HTMLDivElement>) => {
    if (!dragging.current) return
    dragging.current = false
    event.currentTarget.releasePointerCapture(event.pointerId)
    const ratio = ratioAt(event.clientX)
    setPreview(null)
    onChange(ratio)
  }

  // Se frena la propagacion: el atajo global de flechas del player tambien
  // escucha, y sin esto un ArrowRight con foco aca saltaria el doble.
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (disabled) return
    if (event.key === 'ArrowLeft' || event.key === 'ArrowDown') {
      event.preventDefault()
      event.stopPropagation()
      onChange(clamp(value - step))
    } else if (event.key === 'ArrowRight' || event.key === 'ArrowUp') {
      event.preventDefault()
      event.stopPropagation()
      onChange(clamp(value + step))
    }
  }

  return (
    <div
      className={`slider ${kind}`}
      role="slider"
      tabIndex={disabled ? -1 : 0}
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(shown * 100)}
      aria-valuetext={valueText}
      aria-disabled={disabled || undefined}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onPointerCancel={onPointerUp}
      onKeyDown={onKeyDown}
    >
      <div className="channel" ref={railRef}>
        {built !== undefined && (
          <div className="built" style={{ width: `${clamp(built) * 100}%` }} />
        )}
        <div className="fill" style={{ width: `${shown * 100}%` }} />
      </div>
      <div className="grip" style={{ left: `${shown * 100}%` }} />
    </div>
  )
}
