/**
 * Iconos del chrome Homeflix Classic.
 *
 * Son las mismas figuras que los artboards dibujan con divs: triangulos y
 * barras rectas, sin curvas ni antialias decorativo. Van en currentColor para
 * que el estado del boton (normal, presionado, deshabilitado) los tina solo.
 */

export function PlayIcon({ size = 14 }) {
  return (
    <svg width={size * 0.8} height={size} viewBox="0 0 8 10" aria-hidden="true">
      <path d="M0 0 L8 5 L0 10 Z" fill="currentColor" />
    </svg>
  )
}

export function PauseIcon() {
  return (
    <svg width="12" height="14" viewBox="0 0 12 14" aria-hidden="true">
      <rect x="0" y="0" width="4" height="14" fill="currentColor" />
      <rect x="8" y="0" width="4" height="14" fill="currentColor" />
    </svg>
  )
}

export function StopIcon() {
  return (
    <svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true">
      <rect x="0" y="0" width="10" height="10" fill="currentColor" />
    </svg>
  )
}

/** Retroceder: triangulo a la izquierda con su tope, como el |◂ del artboard. */
export function BackIcon() {
  return (
    <svg width="9" height="10" viewBox="0 0 9 10" aria-hidden="true">
      <path d="M6 0 L6 10 L0 5 Z" fill="currentColor" />
      <rect x="7" y="0" width="2" height="10" fill="currentColor" />
    </svg>
  )
}

export function ForwardIcon() {
  return (
    <svg width="9" height="10" viewBox="0 0 9 10" aria-hidden="true">
      <rect x="0" y="0" width="2" height="10" fill="currentColor" />
      <path d="M3 0 L9 5 L3 10 Z" fill="currentColor" />
    </svg>
  )
}

/** Volumen: las tres barras crecientes del artboard. */
export function VolumeIcon({ muted = false }) {
  return (
    <svg width="13" height="12" viewBox="0 0 13 12" aria-hidden="true">
      <rect x="0" y="7" width="3" height="5" fill="currentColor" />
      <rect x="5" y="4" width="3" height="8" fill="currentColor" />
      <rect x="10" y="0" width="3" height="12" fill="currentColor" />
      {muted && (
        <line
          x1="0"
          y1="12"
          x2="13"
          y2="0"
          stroke="currentColor"
          strokeWidth="1.5"
        />
      )}
    </svg>
  )
}

/** Pantalla completa: el marco vacio; al salir, con el marco recogido. */
export function FullscreenIcon({ exit = false }) {
  return (
    <svg width="16" height="12" viewBox="0 0 16 12" aria-hidden="true" fill="none">
      <rect
        x="0.75"
        y="0.75"
        width="14.5"
        height="10.5"
        stroke="currentColor"
        strokeWidth="1.5"
      />
      {exit && <rect x="4" y="3" width="8" height="6" fill="currentColor" />}
    </svg>
  )
}

/** Boton de maximizar de la barra de titulo, con su cabecera gruesa. */
export function WindowIcon({ restore = false }) {
  return (
    <svg width="10" height="9" viewBox="0 0 10 9" aria-hidden="true" fill="none">
      <rect
        x="0.5"
        y="0.5"
        width="9"
        height="8"
        stroke="currentColor"
        strokeWidth="1"
      />
      <rect x="0.5" y="0.5" width="9" height="2.5" fill="currentColor" />
      {restore && <rect x="2.5" y="4.5" width="5" height="3" fill="currentColor" />}
    </svg>
  )
}
