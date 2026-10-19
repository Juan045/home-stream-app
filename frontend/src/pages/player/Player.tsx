import {
  Fragment,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
} from 'react'
import type { SubtitleTrack } from '../../api/client'
import { Icon } from '../../components/Icon'
import { channelLabel, formatDelay, formatTime, subtitleLabel } from '../../format'
import { readBool, readNumber, writePref } from '../../prefs'
import { useAutoHide } from './hooks/useAutoHide'
import { useHlsPlayer } from './hooks/useHlsPlayer'
import { useSubtitleDelay } from './hooks/useSubtitleDelay'
import { useVideoState } from './hooks/useVideoState'
import './player.css'

interface Props {
  /** null mientras el asset no sea reproducible: no se pide el master todavia. */
  masterUrl: string | null
  title: string
  /** "Película · 2010", de la ficha. Vacio si no hay ficha. */
  meta: string
  /** `strategy` de la sesion (remux / transcode). null en el modo ?src. */
  strategy: string | null
  subtitles: SubtitleTrack[]
  duration: number
  /** Avance del build, 0 a 1. null cuando ya termino o no hay sesion. */
  buildProgress: number | null
  /** Mensaje ya traducido de un fallo de la API. */
  apiError: string | null
  /** Slug del fallo, que es el contrato estable: va como codigo en el cartel. */
  apiErrorCode: string | null
  /** Sin ningun parametro de entrada: solo el cartel que los explica. */
  noSource?: boolean
}

type Panel = 'subs' | 'set' | null
type Card = 'intro' | 'preparing' | 'api-error' | 'fatal' | 'end' | 'nosource'

const ALWAYS_SUBS_KEY = 'alwaysSubs'
const CUE_SCALE_KEY = 'cueScale'
/** Escala sobre el tamaño base. El rango guardado sigue siendo el de antes (0,5–2). */
const SIZES = [0.75, 1, 1.3, 1.6]
const SPEEDS = [0.5, 0.75, 1, 1.25, 1.5, 2]
// La playlist EVENT no tiene lo que falta generar: el seek se corta un poco
// antes del borde para no pedir un segmento que no existe.
const SEEK_MARGIN = 1.5
// Un "waiting" a esta distancia del borde es haber alcanzado lo generado, no
// un buffer lento. Unos segmentos de margen: el progreso de la API va un poco
// atrasado respecto de la playlist.
const EDGE_MARGIN = 8

const clamp = (v: number, min: number, max: number) => Math.min(max, Math.max(min, v))
const codecName = (c: string) =>
  c.startsWith('avc1') ? 'H.264'
  : c.startsWith('hvc1') || c.startsWith('hev1') ? 'HEVC'
  : c.startsWith('av01') ? 'AV1'
  : c
const speedLabel = (r: number) => (r === 1 ? 'Normal' : `${r}×`)

/** Texto de los cues activos, sin las etiquetas de WebVTT (<i>, <c.x>). */
function activeText(track: TextTrack): string {
  const cues = track.activeCues
  if (!cues) return ''
  const lines: string[] = []
  for (let i = 0; i < cues.length; i++) {
    lines.push((cues[i] as VTTCue).text.replace(/<[^>]+>/g, ''))
  }
  return lines.join('\n')
}

/**
 * Ubica los subtitulos encima de lo que este a la vista abajo: la barra de
 * controles y, si tapa el texto, el panel abierto. Se mide en vez de usar
 * alturas fijas, asi sigue andando si la barra o el panel cambian de alto. Si
 * no entra encima del panel, queda justo debajo de la barra de arriba.
 */
function placeSubs(root: HTMLElement | null) {
  const subs = root?.querySelector<HTMLElement>('.subs')
  if (!root || !subs) return
  const H = root.clientHeight
  const idle = root.classList.contains('idle')
  const bottom = root.querySelector<HTMLElement>('.bottom')
  let lift = H * 0.08
  if (!idle && bottom) lift = Math.max(lift, bottom.offsetHeight + 16)
  const panel = root.querySelector<HTMLElement>('.panel.open')
  const text = subs.firstElementChild
  if (panel && text) {
    const p = panel.getBoundingClientRect()
    const t = text.getBoundingClientRect()
    if (p.left < t.right && p.right > t.left) lift = Math.max(lift, H - panel.offsetTop + 16)
  }
  const top = idle ? 16 : (root.querySelector<HTMLElement>('.top')?.offsetHeight ?? 0) + 8
  subs.style.bottom = `${Math.min(lift, H - subs.offsetHeight - top)}px`
}

export function Player({
  masterUrl,
  title,
  meta,
  strategy,
  subtitles,
  duration,
  buildProgress,
  apiError,
  apiErrorCode,
  noSource = false,
}: Props) {
  const rootRef = useRef<HTMLDivElement>(null)
  const videoRef = useRef<HTMLVideoElement>(null)
  const timelineRef = useRef<HTMLDivElement>(null)
  const helpCloseRef = useRef<HTMLButtonElement>(null)
  const player = useHlsPlayer(masterUrl, videoRef)
  const video = useVideoState(videoRef, rootRef, duration)

  // "auto" es no haber elegido todavia: recien ahi manda la preferencia de
  // prender subtitulos sola. Cualquier eleccion explicita la reemplaza y no
  // se vuelve atras, ni siquiera cuando aparece una pista nueva.
  const [subChoice, setSubChoice] = useState<number | null | 'auto'>('auto')
  // Ultimo idioma elegido, para que el atajo C pueda devolverlo.
  const lastSubRef = useRef<number | null>(null)
  const [subDelay, setSubDelay] = useState(0)
  const [alwaysSubs, setAlwaysSubs] = useState(() => readBool(ALWAYS_SUBS_KEY))
  // Lo guardado por el slider viejo (1.15, por ejemplo) se lleva al escalon mas
  // cercano: si no, ninguno de los cuatro aparece elegido.
  const [cueScale, setCueScale] = useState(() => {
    const saved = readNumber(CUE_SCALE_KEY, 1, 0.5, 2)
    return SIZES.reduce((a, b) => (Math.abs(b - saved) < Math.abs(a - saved) ? b : a))
  })
  const [cue, setCue] = useState({ url: '', text: '' })

  // El telon siempre: el boton que lo levanta es tambien el gesto que el
  // navegador pide para reproducir con sonido.
  const [started, setStarted] = useState(false)
  const [panel, setPanel] = useState<Panel>(null)
  const [helpOpen, setHelpOpen] = useState(false)
  const [remaining, setRemaining] = useState(false)
  const [overChrome, setOverChrome] = useState(false)
  const [scrub, setScrub] = useState<number | null>(null)
  const [hover, setHover] = useState({ x: 0, t: 0 })
  const [osd, setOsd] = useState({ text: '', show: false })
  const [flash, setFlash] = useState<{ icon: string; n: number } | null>(null)
  const [seekFlash, setSeekFlash] = useState({ acc: 0, show: false })
  const timers = useRef({ osd: 0, seek: 0, tap: 0, lastTap: 0, panelOnDown: false, mouse: true })

  const readySubs = useMemo(() => subtitles.filter((t) => t.url), [subtitles])
  const currentSub =
    subChoice === 'auto' ? (alwaysSubs ? (readySubs[0]?.index ?? null) : null) : subChoice
  const subUrl = useMemo(
    () => subtitles.find((t) => t.index === currentSub)?.url ?? null,
    [subtitles, currentSub],
  )
  const subText = cue.url === subUrl ? cue.text : ''

  const pinned = !video.playing || panel !== null || overChrome || scrub !== null || helpOpen
  const { visible, wake, sleep } = useAutoHide(pinned)

  // Lo generado: con la playlist cerrada (VOD) es todo, aunque la sesion
  // todavia no se haya enterado.
  const built = player.complete ? 1 : (buildProgress ?? 1)
  const building = built < 1
  const pct = Math.round((masterUrl ? built : (buildProgress ?? 0)) * 100)
  const d = video.duration
  const maxSeek = building ? Math.max(0, built * d - SEEK_MARGIN) : d
  const shownTime = scrub ?? video.currentTime

  let card: Card | null = null
  if (noSource) card = 'nosource'
  else if (apiError) card = 'api-error'
  else if (player.fatal) card = 'fatal'
  else if (video.ended) card = 'end'
  else if (!started) card = masterUrl ? 'intro' : 'preparing'
  const error = card === 'nosource' || card === 'api-error' || card === 'fatal'
  const atEdge = started && video.buffering && building && video.currentTime >= built * d - EDGE_MARGIN

  useEffect(() => {
    document.title = title ? `${title} · Telón` : 'Telón'
  }, [title])

  // Con el telon cerrado por un error no sigue sonando nada detras.
  useEffect(() => {
    if (error) videoRef.current?.pause()
  }, [error])

  // Los subtitulos los dibuja la pagina: la pista queda "hidden" (se carga y
  // dispara cuechange, pero el navegador no la pinta) y el texto va al overlay.
  useEffect(() => {
    const el = videoRef.current
    if (!el) return
    const tracks = el.textTracks
    for (let i = 0; i < tracks.length; i++) tracks[i].mode = subUrl ? 'hidden' : 'disabled'
    const track = el.querySelector('track')?.track
    if (!subUrl || !track) return
    const onCue = () => setCue({ url: subUrl, text: activeText(track) })
    track.addEventListener('cuechange', onCue)
    return () => track.removeEventListener('cuechange', onCue)
  }, [subUrl])

  useSubtitleDelay(videoRef, subUrl, subDelay)

  useLayoutEffect(() => {
    placeSubs(rootRef.current)
  }, [visible, panel, subText, cueScale, card])

  useEffect(() => {
    const onResize = () => placeSubs(rootRef.current)
    window.addEventListener('resize', onResize)
    return () => window.removeEventListener('resize', onResize)
  }, [])

  useEffect(() => {
    writePref(ALWAYS_SUBS_KEY, alwaysSubs ? '1' : '0')
  }, [alwaysSubs])

  useEffect(() => {
    writePref(CUE_SCALE_KEY, String(cueScale))
  }, [cueScale])

  // Un toque o click fuera del panel lo cierra.
  useEffect(() => {
    if (!panel) return
    const onDown = (e: PointerEvent) => {
      if (!(e.target as Element).closest('.panel, [data-panel]')) setPanel(null)
    }
    document.addEventListener('pointerdown', onDown)
    return () => document.removeEventListener('pointerdown', onDown)
  }, [panel])

  useEffect(() => {
    if (helpOpen) helpCloseRef.current?.focus()
  }, [helpOpen])

  const showOsd = (text: string) => {
    setOsd({ text, show: true })
    clearTimeout(timers.current.osd)
    timers.current.osd = window.setTimeout(() => setOsd((o) => ({ ...o, show: false })), 1300)
  }

  const play = () => {
    if (!masterUrl) return
    setStarted(true)
    void videoRef.current?.play().catch(() => {})
  }

  const togglePlay = () => {
    const el = videoRef.current
    if (!el) return
    if (el.paused || el.ended) play()
    else el.pause()
  }

  const seekTo = (t: number) => video.seek(clamp(t, 0, maxSeek))

  const seekBy = (dx: number) => {
    const el = videoRef.current
    if (!el || !d) return
    seekTo(el.currentTime + dx)
    setSeekFlash((f) => ({
      acc: f.show && Math.sign(f.acc) === Math.sign(dx) ? f.acc + dx : dx,
      show: true,
    }))
    clearTimeout(timers.current.seek)
    timers.current.seek = window.setTimeout(() => setSeekFlash((f) => ({ ...f, show: false })), 650)
  }

  const selectSub = (index: number | null) => {
    setSubChoice(index)
    if (index !== null) lastSubRef.current = index
    const track = subtitles.find((t) => t.index === index)
    showOsd(track ? `Subtítulos: ${subtitleLabel(track)}` : 'Subtítulos desactivados')
  }

  // Atajo C: apaga, o vuelve al ultimo elegido (o al primero listo).
  const toggleCc = () => {
    if (currentSub !== null) return selectSub(null)
    const remembered = lastSubRef.current
    const restore =
      remembered !== null && readySubs.some((t) => t.index === remembered)
        ? remembered
        : (readySubs[0]?.index ?? null)
    if (restore !== null) selectSub(restore)
  }

  const setSpeed = (r: number) => {
    if (videoRef.current) videoRef.current.playbackRate = r
    showOsd(`Velocidad ${r === 1 ? 'normal' : `${r}×`}`)
  }

  const nudgeVolume = (dv: number) => {
    const el = videoRef.current
    if (!el) return
    el.muted = false
    el.volume = clamp(Math.round((el.volume + dv) * 100) / 100, 0, 1)
    showOsd(`Volumen ${Math.round(el.volume * 100)} %`)
  }

  const toggleMute = () => {
    const el = videoRef.current
    if (!el) return
    // Silenciado con la barra en cero: desmutear sin volumen no se oiria.
    if (el.muted && el.volume === 0) el.volume = 0.6
    el.muted = !el.muted
  }

  const togglePip = () => {
    const el = videoRef.current
    if (!el || !document.pictureInPictureEnabled) return
    const op: Promise<unknown> = document.pictureInPictureElement
      ? document.exitPictureInPicture()
      : el.requestPictureInPicture()
    void op.catch(() => {})
  }

  const togglePanel = (name: Exclude<Panel, null>) => {
    setPanel((p) => (p === name ? null : name))
    wake()
  }

  const onKey = (e: KeyboardEvent) => {
    if (e.ctrlKey || e.metaKey || e.altKey) return
    const target = e.target as HTMLElement | null
    if (target && ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)) return
    const k = e.key.length === 1 ? e.key.toLowerCase() : e.key
    if (k === 'Escape') {
      if (helpOpen) setHelpOpen(false)
      else setPanel(null)
      return
    }
    if (k === '?') {
      setHelpOpen((o) => !o)
      wake()
      return
    }
    if (helpOpen) return
    if (!started) {
      // Enter sobre un boton o un link ya hace lo suyo.
      if (k === 'Enter' && target?.closest('button, a')) return
      if (card === 'intro' && (k === ' ' || k === 'Enter' || k === 'k')) {
        e.preventDefault()
        play()
      }
      return
    }
    // Con una tarjeta a la vista (fin o error) mover la posicion reabriria el
    // telon con el video pausado: ahi solo cuentan sus botones.
    if (card !== null) return
    if (/^[0-9]$/.test(k) && d) {
      const to = (Number(k) / 10) * d
      if (to > maxSeek) showOsd('Esa parte todavía no se generó')
      seekTo(to)
      wake()
      return
    }
    const speed = SPEEDS.indexOf(video.rate)
    const actions: Record<string, () => void> = {
      ' ': togglePlay,
      k: togglePlay,
      ArrowLeft: () => seekBy(-10),
      j: () => seekBy(-10),
      ArrowRight: () => seekBy(10),
      l: () => seekBy(10),
      ArrowUp: () => nudgeVolume(0.05),
      ArrowDown: () => nudgeVolume(-0.05),
      m: () => {
        const el = videoRef.current
        if (!el) return
        el.muted = !el.muted
        showOsd(el.muted ? 'Silenciado' : 'Sonido activado')
      },
      f: video.toggleFullscreen,
      c: toggleCc,
      p: togglePip,
      '<': () => setSpeed(SPEEDS[Math.max(0, speed - 1)] ?? 1),
      '>': () => setSpeed(SPEEDS[Math.min(SPEEDS.length - 1, speed + 1)] ?? 1),
    }
    const fn = actions[k]
    if (!fn) return
    // Espacio con foco en un boton lo apretaria ademas de pausar.
    if (k === ' ' && target?.closest('button, a')) target.blur()
    e.preventDefault()
    fn()
    wake()
  }

  // El handler cambia en cada render (video cambia en cada timeupdate): va por
  // ref, para no resuscribir el listener cada vez.
  const onKeyRef = useRef(onKey)
  useEffect(() => {
    onKeyRef.current = onKey
  })
  useEffect(() => {
    const listener = (e: KeyboardEvent) => onKeyRef.current(e)
    window.addEventListener('keydown', listener)
    return () => window.removeEventListener('keydown', listener)
  }, [])

  // ---------- Linea de tiempo ----------
  // Sin miniatura: necesitaria un segundo hls.js bajando segmentos solo para la vista previa.
  const pointTime = (clientX: number) => {
    const r = timelineRef.current?.getBoundingClientRect()
    if (!r || !r.width) return 0
    const x = clamp(clientX - r.left, 0, r.width)
    const t = (x / r.width) * d
    setHover({ x: clamp(x, 70, r.width - 70), t })
    return Math.min(t, maxSeek)
  }
  const endScrub = () => {
    if (scrub === null) return
    seekTo(scrub)
    setScrub(null)
  }

  // ---------- Gestos sobre el video ----------
  const onHitDown = (e: React.PointerEvent) => {
    timers.current.panelOnDown = panel !== null
    timers.current.mouse = e.pointerType === 'mouse'
  }
  const onHitUp = (e: React.PointerEvent<HTMLDivElement>) => {
    // Con un panel abierto, el toque solo lo cierra.
    if (!started || video.ended || timers.current.panelOnDown) return
    if (e.pointerType === 'mouse') return togglePlay()
    const t = timers.current
    const now = Date.now()
    const w = e.currentTarget.clientWidth
    const side = e.clientX < w / 3 ? -1 : e.clientX > (w * 2) / 3 ? 1 : 0
    clearTimeout(t.tap)
    if (now - t.lastTap < 300 && side) {
      seekBy(side * 10)
      t.lastTap = now
      return
    }
    t.lastTap = now
    t.tap = window.setTimeout(() => (visible ? sleep() : wake()), 260)
  }

  const audioNames = player.audioTracks.map((t) => t.label).join(' · ')
  const subNames = readySubs.map(subtitleLabel).join(' · ')
  const shownVolume = video.muted ? 0 : video.volume
  const quality = player.height ? (player.height >= 2000 ? '4K' : `${player.height}p`) : null
  const back = <a className="btn-ghost" href="/">Volver a la cartelera</a>

  const className = [
    'player',
    !started && !error && 'state-intro',
    card && 'curtain-closed',
    error && 'state-error',
    video.ended && 'ended',
    !video.playing && 'paused',
    started && 'started',
    !visible && 'idle',
    video.buffering && 'waiting',
    atEdge && 'at-edge',
    scrub !== null && 'scrubbing',
  ]
    .filter(Boolean)
    .join(' ')

  const facts = [
    ['Resolución', player.height ? `${player.height}p` : null],
    ['Códec', player.videoCodec ? codecName(player.videoCodec) : null],
    ['Origen', strategy ? (strategy === 'remux' ? 'Sin recodificar' : 'Recodificado') : null],
  ].filter((f): f is [string, string] => f[1] !== null)

  const chromeHover = {
    onPointerEnter: () => setOverChrome(true),
    onPointerLeave: () => {
      setOverChrome(false)
      wake()
    },
  }

  return (
    <div
      ref={rootRef}
      className={className}
      style={{ '--sub-scale': cueScale } as CSSProperties}
      onPointerMove={(e) => e.pointerType === 'mouse' && wake()}
      onPointerDown={(e) => {
        if (e.pointerType !== 'mouse' && (e.target as Element).closest('.chrome, .panel')) wake()
      }}
    >
      <video
        ref={videoRef}
        className="main"
        playsInline
        onPlay={(e) => {
          if (started && e.currentTarget.currentTime > 0.3) setFlash((f) => ({ icon: 'play', n: (f?.n ?? 0) + 1 }))
          wake()
        }}
        onPause={(e) => {
          if (!e.currentTarget.ended && scrub === null) setFlash((f) => ({ icon: 'pause', n: (f?.n ?? 0) + 1 }))
          wake()
        }}
        onEnded={() => {
          if (document.fullscreenElement) void document.exitFullscreen().catch(() => {})
        }}
      >
        {/* key: React tiene que reemplazar el elemento, no mutarle el src. Sin
            default: el navegador lo pondria en "showing" y se veria dos veces. */}
        {subUrl && (
          <track
            key={subUrl}
            kind="subtitles"
            src={subUrl}
            srcLang={subtitles.find((t) => t.index === currentSub)?.language || 'und'}
          />
        )}
      </video>
      <div className="shade chrome" />
      <div className="subs" aria-live="polite">
        {subText && (
          <span>
            {subText.split('\n').map((line, i) => (
              <Fragment key={i}>
                {i > 0 && <br />}
                {/* Lo que va entre [corchetes] (efectos, musica) sale en otro color. */}
                {line.split(/(\[[^\]]+\])/).map((part, j) => (j % 2 ? <em key={j}>{part}</em> : part))}
              </Fragment>
            ))}
          </span>
        )}
      </div>
      <div
        className="hit"
        onPointerDown={onHitDown}
        onPointerUp={onHitUp}
        onDoubleClick={() => timers.current.mouse && video.toggleFullscreen()}
      />

      {flash && (
        <div className="flash go" key={flash.n}>
          <Icon name={flash.icon} />
        </div>
      )}
      {([-1, 1] as const).map((side) => (
        <div
          key={side}
          className={`seek-flash ${side < 0 ? 'left' : 'right'}${seekFlash.show && Math.sign(seekFlash.acc) === side ? ' show' : ''}`}
        >
          <Icon name="replay" />
          <span>{Math.sign(seekFlash.acc) === side && `${side > 0 ? '+' : '−'}${Math.abs(seekFlash.acc)} s`}</span>
        </div>
      ))}
      <button className="bigplay" type="button" aria-label="Reanudar" onClick={play}>
        <Icon name="play" />
      </button>
      <div className="spinner" aria-hidden="true" />
      <div className="edge-note" role="status">
        {atEdge && <>Alcanzaste lo generado · <b>{pct}%</b></>}
      </div>
      <div className={`osd${osd.show ? ' show' : ''}`} role="status">{osd.text}</div>

      <header className="top chrome" {...chromeHover}>
        <a className="back" href="/" aria-label="Volver a la cartelera">
          <span className="c"><Icon name="chev-left" /></span>
          <span className="label">Cartelera</span>
        </a>
        <div className="now">
          <span className="eyebrow">Ahora en escena</span>
          <h1>{title}</h1>
          <span className="scene-name">
            {meta}
            {building && masterUrl && <>{meta && ' · '}<b>generando {pct}%</b></>}
          </span>
        </div>
        <div className="spacer" />
        {masterUrl && quality && <span className="badge">{quality}</span>}
        <button
          className="ctl help-btn"
          type="button"
          aria-label="Atajos de teclado"
          title="Atajos (?)"
          onClick={() => setHelpOpen(true)}
        >
          <Icon name="keys" />
        </button>
      </header>

      <div className="bottom chrome" {...chromeHover}>
        <div
          ref={timelineRef}
          className="timeline"
          role="slider"
          tabIndex={0}
          aria-label="Línea de tiempo"
          aria-valuemin={0}
          aria-valuemax={Math.floor(d)}
          aria-valuenow={Math.floor(shownTime)}
          aria-valuetext={`${formatTime(shownTime)} de ${formatTime(d)}`}
          onPointerMove={(e) => {
            if (!d) return
            const t = pointTime(e.clientX)
            if (scrub !== null) setScrub(t)
          }}
          onPointerDown={(e) => {
            if (!d) return
            e.currentTarget.setPointerCapture(e.pointerId)
            setScrub(pointTime(e.clientX))
          }}
          onPointerUp={endScrub}
          onPointerCancel={endScrub}
        >
          <div className="track">
            <div className="built" style={{ width: `${clamp(built, 0, 1) * 100}%` }} />
            <div className="buf" style={{ width: `${d ? clamp(Math.min(video.bufferedEnd, built * d) / d, 0, 1) * 100 : 0}%` }} />
            <div className="fill" style={{ width: `${d ? clamp(shownTime / d, 0, 1) * 100 : 0}%` }} />
          </div>
          <div className="knob" style={{ left: `${d ? clamp(shownTime / d, 0, 1) * 100 : 0}%` }} />
          <div className="preview" style={{ left: hover.x }}>
            <span className="pv-chap">{hover.t > maxSeek ? 'Todavía no generado' : ''}</span>
            <span className="pv-time">{formatTime(hover.t)}</span>
          </div>
        </div>
        <div className="row">
          <button
            className="ctl play"
            type="button"
            aria-label={video.playing ? 'Pausar' : 'Reproducir'}
            title={video.playing ? 'Pausar (espacio)' : 'Reproducir (espacio)'}
            onClick={togglePlay}
          >
            <Icon name={video.playing ? 'pause' : 'play'} />
          </button>
          <button className="ctl" type="button" aria-label="Retroceder 10 segundos" title="−10 s (←)" onClick={() => seekBy(-10)}>
            <svg className="i" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 12a8 8 0 1 0 2.5-5.8" /><path d="M4 4v4h4" /><text className="num" x="12.2" y="14.8" textAnchor="middle">10</text></svg>
          </button>
          <button className="ctl" type="button" aria-label="Avanzar 10 segundos" title="+10 s (→)" onClick={() => seekBy(10)}>
            <svg className="i" viewBox="0 0 24 24" aria-hidden="true"><path d="M20 12a8 8 0 1 1-2.5-5.8" /><path d="M20 4v4h-4" /><text className="num" x="11.8" y="14.8" textAnchor="middle">10</text></svg>
          </button>
          <div className="vol">
            <button
              className="ctl"
              type="button"
              aria-label={video.muted ? 'Activar sonido' : 'Silenciar'}
              title="Silenciar (M)"
              onClick={toggleMute}
            >
              <Icon name={shownVolume === 0 ? 'vol-mute' : shownVolume < 0.5 ? 'vol-low' : 'vol-high'} />
            </button>
            <input
              type="range"
              min={0}
              max={1}
              step={0.01}
              value={shownVolume}
              aria-label="Volumen"
              style={{ '--v': `${shownVolume * 100}%` } as CSSProperties}
              onChange={(e) => video.setVolume(Number(e.target.value))}
            />
          </div>
          <button className="time" type="button" title="Cambiar a tiempo restante" onClick={() => setRemaining((r) => !r)}>
            {remaining ? `−${formatTime(d - shownTime)}` : formatTime(shownTime)} <span>/ {formatTime(d)}</span>
          </button>
          {video.rate !== 1 && <span className="rate">{video.rate}×</span>}
          <div className="spacer" />
          {building && masterUrl && (
            <span className="gen" title="El servidor sigue generando el video">
              <span className="meter"><span style={{ width: `${pct}%` }} /></span>
              <span className="lbl">Generado {pct}%</span>
            </span>
          )}
          <button
            className={`ctl cc${currentSub !== null ? ' on' : ''}`}
            type="button"
            data-panel
            aria-label="Audio y subtítulos"
            aria-expanded={panel === 'subs'}
            title="Audio y subtítulos (C)"
            onClick={() => togglePanel('subs')}
          >
            <Icon name="cc" />
          </button>
          <button
            className="ctl"
            type="button"
            data-panel
            aria-label="Velocidad y video"
            aria-expanded={panel === 'set'}
            title="Ajustes"
            onClick={() => togglePanel('set')}
          >
            <Icon name="sliders" />
          </button>
          {document.pictureInPictureEnabled && (
            <button className="ctl pip" type="button" aria-label="Imagen en imagen" title="Imagen en imagen (P)" onClick={togglePip}>
              <Icon name="pip" />
            </button>
          )}
          <button
            className="ctl"
            type="button"
            aria-label={video.fullscreen ? 'Salir de pantalla completa' : 'Pantalla completa'}
            title="Pantalla completa (F)"
            onClick={video.toggleFullscreen}
          >
            <Icon name={video.fullscreen ? 'full-exit' : 'full'} />
          </button>
        </div>
      </div>

      {/* Se aplican al instante: no hay Aceptar ni Cancelar. */}
      <div className={`panel panel-subs${panel === 'subs' ? ' open' : ''}`} role="dialog" aria-label="Audio y subtítulos">
        <div className="cols">
          <div>
            <h3>Audio</h3>
            {player.audioTracks.length === 0 ? (
              <p className="note">Este video trae una sola pista de audio.</p>
            ) : (
              <div role="menu">
                {player.audioTracks.map((t) => (
                  <button
                    key={t.id}
                    className="opt"
                    type="button"
                    role="menuitemradio"
                    aria-checked={player.currentAudio === t.id}
                    onClick={() => {
                      // Cambiar de idioma es esto y nada mas: no se toca el servidor.
                      player.selectAudio(t.id)
                      showOsd(`Audio: ${t.label}`)
                    }}
                  >
                    <Icon name="check" />
                    <span>{t.label}</span>
                    {channelLabel(t.channels) && <small>{channelLabel(t.channels)}</small>}
                  </button>
                ))}
              </div>
            )}
          </div>
          <div>
            <h3>Subtítulos</h3>
            <div role="menu">
              {subtitles.map((t) => (
                // Una pista sin url todavia se esta extrayendo: se muestra
                // deshabilitada, no se oculta, para que la lista no salte.
                <button
                  key={t.index}
                  className="opt"
                  type="button"
                  role="menuitemradio"
                  aria-checked={currentSub === t.index}
                  disabled={!t.url}
                  onClick={() => selectSub(t.index)}
                >
                  <Icon name="check" />
                  <span>{subtitleLabel(t)}</span>
                  {!t.url && <small>generando…</small>}
                </button>
              ))}
              <button
                className="opt"
                type="button"
                role="menuitemradio"
                aria-checked={currentSub === null}
                onClick={() => selectSub(null)}
              >
                <Icon name="check" />
                <span>Desactivados</span>
              </button>
            </div>
          </div>
        </div>
        <div className="opt-row">
          <h3>Tamaño</h3>
          <div className="chips">
            {SIZES.map((s, i) => (
              <button
                key={s}
                className="chip"
                type="button"
                aria-pressed={s === cueScale}
                aria-label={`Tamaño ${Math.round(s * 100)} %`}
                style={{ fontSize: 12 + i * 3 }}
                onClick={() => setCueScale(s)}
              >
                A
              </button>
            ))}
          </div>
        </div>
        <div className="opt-row">
          <h3>Sincronía</h3>
          <div className="stepper">
            <button className="chip" type="button" aria-label="Adelantar subtítulos 0,1 s" onClick={() => setSubDelay((v) => Math.round((v - 0.1) * 10) / 10)}>
              <Icon name="minus" />
            </button>
            <output>{formatDelay(subDelay)}</output>
            <button className="chip" type="button" aria-label="Retrasar subtítulos 0,1 s" onClick={() => setSubDelay((v) => Math.round((v + 0.1) * 10) / 10)}>
              <Icon name="plus" />
            </button>
            <button className="chip" type="button" onClick={() => setSubDelay(0)}>A cero</button>
          </div>
        </div>
        <div className="opt-row">
          <h3>Activar siempre</h3>
          <button
            className="switch"
            type="button"
            role="switch"
            aria-checked={alwaysSubs}
            aria-label="Mostrar subtítulos siempre que haya una pista"
            onClick={() => setAlwaysSubs((v) => !v)}
          />
        </div>
      </div>

      <div className={`panel panel-set${panel === 'set' ? ' open' : ''}`} role="dialog" aria-label="Velocidad y video">
        <h3>Velocidad</h3>
        <div className="chips">
          {SPEEDS.map((s) => (
            <button key={s} className="chip" type="button" aria-pressed={s === video.rate} onClick={() => setSpeed(s)}>
              {speedLabel(s)}
            </button>
          ))}
        </div>
        <h3>Video</h3>
        <dl className="facts">
          {facts.map(([k, v]) => (
            <Fragment key={k}>
              <dt>{k}</dt>
              <dd>{v}</dd>
            </Fragment>
          ))}
        </dl>
        <p className="note">El servidor entrega una sola calidad: no hay versiones alternativas que elegir.</p>
      </div>

      <div className="curtain left" />
      <div className="curtain right" />
      <div className="valance" />
      <div className="footlights" />
      <div className="card">
        <div className="card-box" role={error ? 'alert' : undefined}>
          {card === 'intro' && (
            <>
              <span className="marquee">Telón presenta</span>
              <h2>{title}</h2>
              <p>
                {[meta, audioNames && `Audio ${audioNames}`, subNames && `Subtítulos ${subNames}`]
                  .filter(Boolean)
                  .join(' · ')}
              </p>
              {building && (
                <p className="hint">
                  El servidor sigue generando el video ({pct}%). Podés empezar ya: se ve hasta donde vaya.
                </p>
              )}
              <div className="card-actions">
                <button className="btn-primary raise" type="button" onClick={play}>
                  <Icon name="play" />
                  Levantar el telón
                </button>
              </div>
              <p className="hint">Pulsá <kbd>?</kbd> para ver los atajos de teclado</p>
            </>
          )}
          {card === 'preparing' && (
            <>
              <span className="marquee">Preparando la función</span>
              <h2>{title}</h2>
              <p>Esperando los primeros segmentos del servidor.</p>
              <div className="prep">
                <div
                  className={`bar${pct === 0 ? ' queued' : ''}`}
                  role="progressbar"
                  aria-label="Preparando la función"
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-valuenow={pct}
                >
                  <span style={{ width: `${pct}%` }} />
                </div>
                <small>{pct === 0 ? 'En cola' : `${pct}%`}</small>
              </div>
              <div className="card-actions">
                <button className="btn-primary raise" type="button" disabled>
                  <Icon name="play" />
                  Levantar el telón
                </button>
              </div>
            </>
          )}
          {card === 'api-error' && (
            <>
              <span className="marquee">Problema técnico</span>
              <h2>No pudimos levantar el telón</h2>
              <p>{apiError}</p>
              {apiErrorCode && <span className="code">{apiErrorCode}</span>}
              <div className="card-actions">
                <button className="btn-primary" type="button" onClick={() => location.reload()}>
                  <Icon name="replay" />
                  Recargar
                </button>
                {back}
              </div>
            </>
          )}
          {card === 'fatal' && (
            <>
              <span className="marquee">Problema técnico</span>
              <h2>Se cortó la función</h2>
              <p>Se perdió la conexión con el servidor de streaming. La posición quedó guardada.</p>
              {player.fatalDetail && <span className="code">{player.fatalDetail}</span>}
              <div className="card-actions">
                {/* Retoma la posicion: el hook la guarda antes de reconstruir hls.js. */}
                <button className="btn-primary" type="button" onClick={player.reload}>
                  <Icon name="replay" />
                  Reintentar
                </button>
                {back}
              </div>
            </>
          )}
          {card === 'end' && (
            <>
              <span className="marquee">Fin de la función</span>
              <h2>Gracias por venir</h2>
              <p>Esperamos que la hayas disfrutado. La sala queda abierta si querés repetir.</p>
              <div className="card-actions">
                <button className="btn-ghost" type="button" onClick={() => { video.seek(0); play() }}>
                  <Icon name="replay" />
                  Volver a ver
                </button>
                <a className="btn-primary" href="/">Volver a la cartelera</a>
              </div>
            </>
          )}
          {card === 'nosource' && (
            <>
              <span className="marquee">Sin función</span>
              <h2>No hay nada en cartelera</h2>
              <p>Abrí el reproductor con uno de estos parámetros:</p>
              <div className="params">
                <code>?media=&lt;id de una ficha&gt;</code>
                <code>?file=/ruta/absoluta/al/video.mkv</code>
                <code>?session=&lt;id de sesión&gt;</code>
                <code>?src=&lt;url de un master.m3u8&gt;</code>
              </div>
              <div className="card-actions">
                <a className="btn-primary" href="/">Volver a la cartelera</a>
              </div>
            </>
          )}
        </div>
      </div>

      <div
        className={`help${helpOpen ? ' open' : ''}`}
        role="dialog"
        aria-modal="true"
        aria-label="Atajos de teclado"
        onClick={(e) => e.target === e.currentTarget && setHelpOpen(false)}
      >
        <div className="help-box">
          <div className="help-head">
            <h2>Atajos de teclado</h2>
            <button ref={helpCloseRef} className="ctl" type="button" aria-label="Cerrar" onClick={() => setHelpOpen(false)}>
              <Icon name="x" />
            </button>
          </div>
          <dl>
            <div><dt>Reproducir / pausa</dt><dd><kbd>Espacio</kbd><kbd>K</kbd></dd></div>
            <div><dt>±10 segundos</dt><dd><kbd>←</kbd><kbd>→</kbd></dd></div>
            <div><dt>Volumen</dt><dd><kbd>↑</kbd><kbd>↓</kbd></dd></div>
            <div><dt>Silenciar</dt><dd><kbd>M</kbd></dd></div>
            <div><dt>Subtítulos</dt><dd><kbd>C</kbd></dd></div>
            <div><dt>Pantalla completa</dt><dd><kbd>F</kbd></dd></div>
            <div><dt>Imagen en imagen</dt><dd><kbd>P</kbd></dd></div>
            <div><dt>Velocidad</dt><dd><kbd>&lt;</kbd><kbd>&gt;</kbd></dd></div>
            <div><dt>Ir al 0 % … 90 %</dt><dd><kbd>0</kbd>–<kbd>9</kbd></dd></div>
            <div><dt>Cerrar paneles</dt><dd><kbd>Esc</kbd></dd></div>
          </dl>
        </div>
      </div>
    </div>
  )
}
