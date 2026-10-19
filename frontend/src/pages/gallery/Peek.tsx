/** La ficha flotante: `GET /media/{id}` y el estado de la codificacion AV1. */
import { useEffect, useLayoutEffect, useRef } from 'react'
import type { MediaResponse } from '../../api/client'
import { Icon } from '../../components/Icon'
import { KINDS, MISSING, channelLabel, formatRuntime } from '../../format'
import { useEncode, type EncodeState } from './hooks/useEncode'
import { useMediaDetail } from './hooks/useMediaDetail'
import type { Peek as PeekApi } from './hooks/usePeek'
import { Scene } from './Scene'
import { codec, langs, playerUrl, resolution } from './shared'

interface Props {
  peek: PeekApi
  /** Lo que devolvio el PATCH de Mi lista: pisa a la ficha pedida antes. */
  overrides: Record<string, MediaResponse>
  saving: string | null
  onToggleList: (media: MediaResponse) => void
}

export function Peek({ peek, overrides, saving, onToggleList }: Props) {
  const { state } = peek
  const detail = useMediaDetail(state?.id ?? null)
  // Solo abierta: cerrada no tiene sentido seguir repreguntando el avance.
  const encode = useEncode(state?.show ? state.id : null)
  const media = state ? (overrides[state.id] ?? detail.media) : null
  const ready = media !== null || detail.error !== null
  const shown = state !== null && state.show && ready
  const ref = useRef<HTMLDivElement>(null)

  // Se ubica junto a la tarjeta recien con el contenido puesto: antes no se sabe cuanto mide.
  useLayoutEffect(() => {
    const el = ref.current
    if (!el || !state?.show || state.sheet || !ready) return
    const r = state.anchor.getBoundingClientRect()
    const w = el.offsetWidth, h = el.offsetHeight, m = 12
    const left = clamp(r.left + r.width / 2 - w / 2, m, innerWidth - w - m)
    const top = clamp(r.top - 40, m, Math.max(m, innerHeight - h - m))
    el.style.left = `${left}px`
    el.style.top = `${top}px`
    el.style.setProperty('--ox', `${r.left + r.width / 2 - left}px`)
    el.style.setProperty('--oy', `${r.top + r.height / 2 - top}px`)
  }, [state, ready])

  // Abierta a proposito, el foco entra a la ficha para que Tab llegue a sus botones.
  // Al cerrarse vuelve a lo que la abrio, si no se fue a otro lado mientras tanto.
  const pinned = shown && state.pinned
  const anchor = state?.anchor
  useEffect(() => {
    if (!pinned || !anchor) return
    const el = ref.current
    el?.querySelector<HTMLElement>('.peek-actions a')?.focus()
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
      >
        {media ? (
          <Body media={media} encode={encode} saving={saving === media.id_media} onToggleList={onToggleList} onClose={peek.close} />
        ) : (
          detail.error && <div className="peek-body"><p className="peek-syn">{detail.error}</p></div>
        )}
      </div>
    </>
  )
}

function Body({ media, encode, saving, onToggleList, onClose }: {
  media: MediaResponse
  encode: EncodeState
  saving: boolean
  onToggleList: (media: MediaResponse) => void
  onClose: () => void
}) {
  const audio = langs(media.audio_tracks)
  const subs = langs(media.subtitle_tracks)
  const best = Math.max(...media.audio_tracks.map((t) => t.channels))
  const quality = [
    media.height ? resolution(media.height) : null,
    codec(media.video_codec),
    media.audio_tracks.length ? channelLabel(String(best)) : null,
  ].filter((q) => q !== null)
  const meta = [media.year !== null ? String(media.year) : null, formatRuntime(media.duration), KINDS[media.kind] ?? media.kind].filter((x) => x !== null)

  return (
    <>
      <div className="peek-art">
        <Scene media={media} />
        <div className="peek-title">
          <span className="act">{media.genres[0] ?? KINDS[media.kind]}</span>
          <h3 id="peekTitle">{media.title}</h3>
        </div>
        <button className="peek-close" type="button" aria-label="Cerrar" onClick={onClose}><Icon name="x" /></button>
      </div>
      <div className="peek-body">
        <div className="peek-actions">
          {/* Un <a> y no un boton: cada vista es una pagina propia y reproducir es navegar. */}
          <a className="btn-primary" href={playerUrl(media.id_media)}><Icon name="play" />Ver función</a>
          <button
            className="btn-ghost"
            type="button"
            aria-pressed={media.in_list}
            aria-label={media.in_list ? 'Quitar de Mi lista' : 'Añadir a Mi lista'}
            disabled={saving}
            onClick={() => onToggleList(media)}
          >
            <Icon name={media.in_list ? 'check' : 'plus'} />
          </button>
        </div>
        <div className="peek-meta">
          {meta.flatMap((part, i) => [i > 0 && <span className="sep" key={`s${i}`} />, <span key={i}>{part}</span>])}
        </div>
        {/* La ficha incompleta se muestra: que falte un dato es informacion. */}
        <p className="peek-syn">{media.synopsis ?? <span className="missing">{MISSING}</span>}</p>
        <dl className="credits">
          <dt>Género</dt>
          <dd>{media.genres.length ? media.genres.join(', ') : <span className="missing">{MISSING}</span>}</dd>
          <dt>Calidad</dt>
          <dd><div className="quality">{quality.map((q, i) => <span className={i === 0 ? 'q top' : 'q'} key={q}>{q}</span>)}</div></dd>
          <dt>Idiomas</dt>
          <dd>Audio {audio || '—'}{subs && ` · Subt. ${subs}`}</dd>
          <dt>Biblioteca</dt>
          <dd><Library state={encode} /></dd>
        </dl>
        <a className="btn-edit" href={`/edit/?media=${encodeURIComponent(media.id_media)}`}><Icon name="edit" />Editar ficha</a>
      </div>
    </>
  )
}

/**
 * La codificacion de biblioteca. Cinco estados y una sola accion.
 *
 * El boton sale con `idle` y `pending`: en los dos el video todavia no se
 * despacho (el prewarm del PATCH deja `pending` con solo audio y subtitulos).
 * No hay boton de reintentar ni de recodificar: con `building`, `ready` o
 * `failed` el `POST` contesta 409. Un boton que solo puede dar error miente
 * sobre lo que el servidor deja hacer.
 */
function Library({ state }: { state: EncodeState }) {
  const { encode, error, starting, start } = state

  if (error !== null) return <div className="lib failed">{error}</div>
  if (encode === null) return null
  if (encode.state === 'ready') return <div className="lib ready">Versión AV1 lista</div>
  if (encode.state === 'failed') return <div className="lib failed">Falló. Hay que borrarla del cache para reintentar.</div>

  if (encode.state === 'building') {
    return (
      <div className="lib">
        {/* `building` se marca antes de que FFmpeg arranque, incluida la espera de un slot: sin avance es "en cola". */}
        <div className="row">{encode.progress === 0 ? 'En cola…' : `Codificando AV1 · ${Math.round(encode.progress * 100)}%`}</div>
        <Bar progress={encode.progress} />
        <span className="hint">Se puede ver mientras tanto, hasta donde vaya.</span>
      </div>
    )
  }

  return (
    <div className="lib">
      <button className="btn-encode" type="button" onClick={start} disabled={starting}>
        <Icon name="chip" />{starting ? 'Iniciando…' : 'Codificar AV1'}
      </button>
      <span className="hint">Tarda horas. El servidor sigue aunque cierres la página.</span>
    </div>
  )
}

/** El avance AV1. Sin avance ("en cola") la barra va indeterminada. */
export function Bar({ progress }: { progress: number }) {
  const percent = Math.round(progress * 100)
  const queued = progress === 0
  return (
    <div
      className={queued ? 'progress queued' : 'progress'}
      role="progressbar"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={queued ? undefined : percent}
      aria-label="Codificación"
    >
      <span style={{ width: `${percent}%` }} />
    </div>
  )
}

const clamp = (v: number, a: number, b: number) => Math.min(b, Math.max(a, v))
