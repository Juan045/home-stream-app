/** La ficha flotante: el poster, si se puede ver ya y la entrada al detalle. */
import { useEffect, useLayoutEffect, useRef } from 'react'
import type { MediaResponse } from '../../api/client'
import { Icon } from '../../components/Icon'
import { Artwork } from './Artwork'
import { availability } from './availability'
import { useEncode } from './hooks/useEncode'
import { useMediaDetail } from './hooks/useMediaDetail'
import type { Peek as PeekApi } from './hooks/usePeek'

interface Props {
  peek: PeekApi
  /** "Mas info" abre el detalle, que vive en la pagina. */
  onDetail: (media: MediaResponse) => void
}

export function Peek({ peek, onDetail }: Props) {
  const { state } = peek
  // El detalle necesita la ficha entera, asi que se pide aunque aca se muestre poco.
  const { media, error } = useMediaDetail(state?.id ?? null)
  // Solo abierta: cerrada no tiene sentido seguir repreguntando el avance.
  const avail = availability(useEncode(state?.show ? state.id : null).encode)
  const ready = media !== null || error !== null
  const shown = state !== null && state.show && ready
  const ref = useRef<HTMLDivElement>(null)

  // Se centra sobre la tarjeta recien con el contenido puesto: antes no se sabe cuanto mide.
  useLayoutEffect(() => {
    const el = ref.current
    if (!el || !state?.show || state.sheet || !ready) return
    const r = state.anchor.getBoundingClientRect()
    const w = el.offsetWidth, h = el.offsetHeight, m = 12
    const left = clamp(r.left + r.width / 2 - w / 2, m, innerWidth - w - m)
    const top = clamp(r.top + r.height / 2 - h / 2, m, Math.max(m, innerHeight - h - m))
    el.style.left = `${left}px`
    el.style.top = `${top}px`
    el.style.setProperty('--ox', `${r.left + r.width / 2 - left}px`)
    el.style.setProperty('--oy', `${r.top + r.height / 2 - top}px`)
  }, [state, ready])

  // Abierta a proposito, el foco va a "Mas info".
  // Al cerrarse vuelve a lo que la abrio, si no se fue a otro lado mientras tanto.
  const pinned = shown && state.pinned
  const anchor = state?.anchor
  useEffect(() => {
    if (!pinned || !anchor) return
    const el = ref.current
    el?.querySelector<HTMLElement>('.peek-more')?.focus()
    return () => {
      if (el?.contains(document.activeElement)) anchor.focus()
    }
  }, [pinned, anchor])

  const sheet = state?.sheet ?? false

  return (
    <>
      <div className={shown && sheet ? 'scrim peek-scrim show' : 'scrim peek-scrim'} onClick={peek.close} />
      <div
        ref={ref}
        className={`peek${sheet ? ' sheet' : ''}${shown ? ' show' : ''}`}
        role="dialog"
        aria-hidden={!shown}
        aria-labelledby="peekTitle"
        onMouseEnter={peek.keep}
        onMouseLeave={() => {
          if (!sheet) peek.scheduleHide()
        }}
        onKeyDown={(e) => {
          // Tab sale por la tarjeta que la abrio: el navegador sigue desde ahi y el
          // recorrido no salta al principio de la pagina. Shift+Tab se queda en la tarjeta.
          if (e.key !== 'Tab' || !state) return
          if (e.shiftKey) e.preventDefault()
          peek.close()
          state.anchor.focus()
        }}
      >
        {media ? (
          <>
            <div className="peek-art">
              <Artwork media={media} portrait />
              {/* Sin el titulo, una escena generada no dice de que ficha es. */}
              <div className="peek-title"><h3 id="peekTitle">{media.title}</h3></div>
              <button className="peek-close" type="button" aria-label="Cerrar" onClick={peek.close}><Icon name="x" /></button>
            </div>
            <div className="peek-foot">
              <p className="peek-avail" data-tone={avail.tone}>{avail.label}</p>
              <button className="peek-more" type="button" onClick={() => onDetail(media)}><Icon name="info" />Más info</button>
            </div>
          </>
        ) : (
          error && <p className="peek-foot">{error}</p>
        )}
      </div>
    </>
  )
}

const clamp = (v: number, a: number, b: number) => Math.min(b, Math.max(a, v))
