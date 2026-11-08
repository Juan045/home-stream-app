/**
 * Cuando se abre y se cierra la ficha flotante.
 *
 * Con mouse se abre al pasar sobre la tarjeta, con una demora para que barrer
 * la grilla no la dispare; con teclado, con el foco visible, y Tab entra. En pantalla
 * tactil no hay hover: el primer toque la abre como hoja de abajo en vez de
 * reproducir y queda fija hasta tocar fuera.
 */
import { useCallback, useEffect, useRef, useState, type FocusEvent, type KeyboardEvent as KeyEvent, type MouseEvent } from 'react'

const canHover = matchMedia('(hover: hover) and (pointer: fine)').matches

export interface PeekState {
  id: string
  anchor: HTMLElement
  sheet: boolean
  /** Abierta a proposito (un toque o Tab desde la tarjeta): la ficha toma el foco. */
  pinned: boolean
  /** Cerrada conserva el resto, asi se desvanece en su lugar con su contenido. */
  show: boolean
}

export function usePeek() {
  const [state, setState] = useState<PeekState | null>(null)
  const showTimer = useRef(0)
  const hideTimer = useRef(0)
  const pinned = useRef(false)

  const close = useCallback(() => {
    clearTimeout(showTimer.current)
    pinned.current = false
    setState((s) => (s && s.show ? { ...s, show: false } : s))
  }, [])

  const open = (id: string, anchor: HTMLElement, sheet: boolean, pin = false) => {
    clearTimeout(showTimer.current)
    clearTimeout(hideTimer.current)
    pinned.current = pin
    setState({ id, anchor, sheet, pinned: pin, show: true })
  }

  const scheduleHide = () => {
    clearTimeout(showTimer.current)
    if (!pinned.current) hideTimer.current = setTimeout(close, 160)
  }

  const keep = () => clearTimeout(hideTimer.current)

  /** Los handlers de una tarjeta que abre la ficha. */
  const card = (id: string) => ({
    'data-peek': id,
    onMouseEnter: (e: MouseEvent<HTMLElement>) => {
      if (!canHover) return
      const el = e.currentTarget
      clearTimeout(hideTimer.current)
      clearTimeout(showTimer.current)
      // Ya abierta, pasar a la tarjeta vecina es casi inmediato.
      showTimer.current = setTimeout(() => open(id, el, false), state?.show ? 500 : 1000)
    },
    onMouseLeave: () => {
      if (canHover) scheduleHide()
    },
    onFocus: (e: FocusEvent<HTMLElement>) => {
      const el = e.currentTarget
      if (!el.matches(':focus-visible')) return
      clearTimeout(showTimer.current)
      showTimer.current = setTimeout(() => open(id, el, !canHover), 10)
    },
    onBlur: scheduleHide,
    // El foco solo la muestra; Tab entra a "Mas info", el unico camino al detalle con teclado.
    // Sin el boton todavia (cargando o con error) Tab sigue de largo.
    onKeyDown: (e: KeyEvent<HTMLElement>) => {
      if (e.key !== 'Tab' || e.shiftKey || !state?.show || state.id !== id) return
      if (!document.querySelector('.peek .peek-more')) return
      e.preventDefault()
      open(id, e.currentTarget, state.sheet, true)
    },
    onClick: (e: MouseEvent<HTMLElement>) => {
      if (canHover) return
      e.preventDefault()
      open(id, e.currentTarget, true, true)
    },
  })

  useEffect(() => {
    if (!state?.show) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') close()
    }
    const onDown = (e: PointerEvent) => {
      const target = e.target as Element
      if (pinned.current && !target.closest('.peek, [data-peek]')) close()
    }
    // Flotante va fija a la pantalla: con scroll quedaria lejos de su tarjeta.
    const onScroll = () => {
      if (!state.sheet) close()
    }
    document.addEventListener('keydown', onKey)
    document.addEventListener('pointerdown', onDown)
    addEventListener('scroll', onScroll, { passive: true })
    return () => {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('pointerdown', onDown)
      removeEventListener('scroll', onScroll)
    }
  }, [state, close])

  return { state, card, close, keep, scheduleHide }
}

export type Peek = ReturnType<typeof usePeek>
