/**
 * Cuando se abre y se cierra la ficha flotante.
 *
 * Con mouse se abre al pasar sobre la tarjeta, con una demora para que barrer
 * la grilla no la dispare; con teclado, con el foco visible. En pantalla
 * tactil no hay hover: el primer toque la abre como hoja de abajo en vez de
 * reproducir. Abierta desde el boton de la ficha queda fija hasta tocar fuera.
 */
import { useCallback, useEffect, useRef, useState, type FocusEvent, type MouseEvent } from 'react'

const canHover = matchMedia('(hover: hover) and (pointer: fine)').matches

export interface PeekState {
  id: string
  anchor: HTMLElement
  sheet: boolean
  /** Abierta a proposito (boton de la ficha o toque): la ficha toma el foco. */
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
    onClick: (e: MouseEvent<HTMLElement>) => {
      if (canHover) return
      e.preventDefault()
      open(id, e.currentTarget, true, true)
    },
  })

  /** El boton de la ficha del escenario: abre fija, o cierra si ya estaba. */
  const toggle = (id: string, anchor: HTMLElement) => {
    if (pinned.current && state?.show) return close()
    open(id, anchor, !canHover, true)
  }

  useEffect(() => {
    if (!state?.show) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') close()
    }
    const onDown = (e: PointerEvent) => {
      const target = e.target as Element
      if (pinned.current && !target.closest('.peek, [data-peek], [data-peek-toggle]')) close()
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

  return { state, card, toggle, close, keep, scheduleHide }
}

export type Peek = ReturnType<typeof usePeek>
