/** El escenario: las ultimas agregadas, con un telon que se cierra y abre entre titulos. */
import { useRef, useState } from 'react'
import type { MediaResponse } from '../../api/client'
import { Icon } from '../../components/Icon'
import { KINDS, formatRuntime } from '../../format'
import { useEncode, type EncodeState } from './hooks/useEncode'
import type { Peek } from './hooks/usePeek'
import { Bar } from './Peek'
import { Scene } from './Scene'
import { langs, playerUrl, reduceMotion } from './shared'

interface Props {
  items: MediaResponse[]
  peek: Peek
  saving: string | null
  onToggleList: (media: MediaResponse) => void
}

export function Stage({ items, peek, saving, onToggleList }: Props) {
  const [current, setCurrent] = useState(0)
  const [closed, setClosed] = useState(false)
  const busy = useRef(false)
  const m = items[current]
  // Solo el titulo visible: el estado AV1 de cada tarjeta seria un pedido por titulo.
  const encode = useEncode(m.id_media)
  const [light, bold] = splitTitle(m.title)
  const audio = langs(m.audio_tracks)
  const meta = [
    m.year !== null && <span key="y">{m.year}</span>,
    <span key="d">{formatRuntime(m.duration)}</span>,
    m.genres.length > 0 && <span key="g">{m.genres.join(' · ')}</span>,
    audio && <span className="lang" key="a">{audio}</span>,
  ].filter(Boolean)

  // El cambio va con el telon cerrado: se cierra, se cambia, se abre.
  const goTo = (next: number) => {
    const n = (next + items.length) % items.length
    if (busy.current || n === current) return
    if (reduceMotion) return setCurrent(n)
    busy.current = true
    setClosed(true)
    setTimeout(() => {
      setCurrent(n)
      setTimeout(() => {
        setClosed(false)
        setTimeout(() => (busy.current = false), 750)
      }, 160)
    }, 760)
  }

  return (
    <section className={closed ? 'stage is-closed' : 'stage'} aria-roledescription="carrusel" aria-label="Agregadas recientemente">
      <div className="scene"><Scene media={m} /></div>
      <div className="stage-fade" />
      <div className="stage-content" aria-live="polite">
        <span className="marquee">Nueva · {KINDS[m.kind] ?? m.kind}</span>
        <h1 className="stage-title">
          {light && <span className="t-light">{light}</span>}
          <span className="t-bold">{bold}</span>
        </h1>
        <div className="meta">{meta.flatMap((part, i) => (i > 0 ? [<span className="sep" key={`s${i}`} />, part] : [part]))}</div>
        {m.synopsis && <p className="tagline">{m.synopsis}</p>}
        <div className="actions">
          <a className="btn-primary" href={playerUrl(m.id_media)}><Icon name="play" />Ver función</a>
          <button
            className="btn-ghost"
            type="button"
            aria-pressed={m.in_list}
            aria-label={m.in_list ? 'Quitar de Mi lista' : 'Añadir a Mi lista'}
            title={m.in_list ? 'En tu lista' : 'Añadir a Mi lista'}
            disabled={saving === m.id_media}
            onClick={() => onToggleList(m)}
          >
            <Icon name={m.in_list ? 'check' : 'plus'} />
          </button>
          <button className="btn-ghost" type="button" data-peek-toggle aria-label="Ver ficha" title="Ficha" onClick={(e) => peek.toggle(m.id_media, e.currentTarget)}>
            <Icon name="info" />
          </button>
          <HeroEncode state={encode} />
        </div>
      </div>
      <div className="stage-thumbs">
        <button className="round" type="button" aria-label="Anterior" onClick={() => goTo(current - 1)}><Icon name="chev-left" /></button>
        <div className="thumbs-row">
          {items.map((item, i) => (
            <button className="thumb" type="button" key={item.id_media} aria-label={`Ver ${item.title}`} aria-current={i === current} onClick={() => goTo(i)}>
              <Scene media={item} />
            </button>
          ))}
        </div>
        <button className="round" type="button" aria-label="Siguiente" onClick={() => goTo(current + 1)}><Icon name="chev-right" /></button>
      </div>
      <div className="footlights" />
      <div className="curtain left" />
      <div className="curtain right" />
      <div className="valance" />
    </section>
  )
}

/** Donde el diseño tenia "+5 amigos en la sala": la codificacion de biblioteca. */
function HeroEncode({ state }: { state: EncodeState }) {
  const { encode } = state
  if (encode === null) return null
  if (encode.state === 'building') {
    return (
      <span className="enc">
        <span className="meter"><Bar progress={encode.progress} /></span>
        {encode.progress === 0 ? 'En cola para AV1…' : <>Codificando AV1 · <strong>{Math.round(encode.progress * 100)}%</strong></>}
      </span>
    )
  }
  if (encode.state === 'ready') return <span className="enc ready"><Icon name="chip" />Versión AV1 de biblioteca</span>
  if (encode.state === 'failed') return <span className="enc failed">La codificación falló</span>
  return null
}

/** La parte en negrita del titulo: lo que sigue a ":" o el ultimo tercio de las palabras. */
function splitTitle(t: string): [string, string] {
  const colon = t.match(/^(.+?):\s*(.+)$/)
  if (colon) return [colon[1], colon[2]]
  const words = t.split(' ')
  if (words.length === 1) return ['', t]
  const cut = Math.max(1, words.length - Math.ceil(words.length / 3))
  return [words.slice(0, cut).join(' '), words.slice(cut).join(' ')]
}
