/**
 * Los controles se desvanecen solos. Se quedan visibles mientras el video esta
 * en pausa o hay un panel abierto: esconder controles que el usuario acaba de
 * abrir es peor que dejarlos.
 */
import { useCallback, useEffect, useState } from 'react'

const IDLE_MS = 3000

export function useAutoHide(pinned: boolean): {
  visible: boolean
  wake: () => void
  /** Esconder ya (un toque en la pantalla tactil). Con pinned no hace nada. */
  sleep: () => void
} {
  const [idle, setIdle] = useState(false)
  const [awakeAt, setAwakeAt] = useState(0)

  const wake = useCallback(() => {
    setIdle(false)
    setAwakeAt((n) => n + 1)
  }, [])

  const sleep = useCallback(() => setIdle(true), [])

  useEffect(() => {
    // Con pinned no hay temporizador: la visibilidad se deriva abajo, sin
    // escribir estado desde el efecto.
    if (pinned) return
    const timer = setTimeout(() => setIdle(true), IDLE_MS)
    return () => clearTimeout(timer)
  }, [pinned, awakeAt])

  return { visible: pinned || !idle, wake, sleep }
}
