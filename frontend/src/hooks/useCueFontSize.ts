/**
 * Tamaño en pixeles de los subtitulos.
 *
 * La base es proporcional a la altura de la pantalla, no un valor fijo: con
 * pixeles fijos el mismo texto queda enorme en una ventana chica e ilegible en
 * pantalla completa. Es lo que hace el navegador por defecto con los cues
 * (~5% del alto del video); aca se replica para poder multiplicarlo por la
 * escala que elige el espectador.
 */
import { useEffect, useState } from 'react'

/** Fraccion del alto de la pantalla que ocupa un renglon al 100%. */
const BASE_RATIO = 0.045
const MIN_PX = 12
const MAX_PX = 72

export function useCueFontSize(
  screenRef: React.RefObject<HTMLElement | null>,
  scale: number,
): { basePx: number; cuePx: number } {
  const [height, setHeight] = useState(0)

  useEffect(() => {
    const element = screenRef.current
    if (!element) return

    // ResizeObserver y no el evento resize: la pantalla tambien cambia de alto
    // al entrar en pantalla completa, sin que cambie el tamaño de la ventana.
    const observer = new ResizeObserver((entries) => {
      setHeight(entries[0].contentRect.height)
    })
    observer.observe(element)
    return () => observer.disconnect()
  }, [screenRef])

  const basePx = Math.min(MAX_PX, Math.max(MIN_PX, Math.round(height * BASE_RATIO)))
  return { basePx, cuePx: Math.round(basePx * scale) }
}
