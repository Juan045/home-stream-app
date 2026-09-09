import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { SubtitleTrack } from '../api/client'
import { channelLabel, formatTime, subtitleLabel } from '../format'
import { readBool, readNumber, writePref } from '../prefs'
import { useAutoHide } from '../hooks/useAutoHide'
import { useCueFontSize } from '../hooks/useCueFontSize'
import { useHlsPlayer } from '../hooks/useHlsPlayer'
import { useSubtitleDelay } from '../hooks/useSubtitleDelay'
import { useVideoState } from '../hooks/useVideoState'
import { BuildProgress } from './BuildProgress'
import { ControlBar, type OpenMenu } from './ControlBar'
import { PlaybackError } from './PlaybackError'
import {
  MAX_SIZE,
  MIN_SIZE,
  SettingsDialog,
  type SubtitleSettings,
} from './SettingsDialog'
import { StatusBar } from './StatusBar'
import { TitleBar } from './TitleBar'
import { PlayIcon } from './icons'
import type { MenuItem } from './TrackMenu'

interface Props {
  /** null mientras el asset no sea reproducible: no se pide el master todavia. */
  masterUrl: string | null
  title: string
  subtitles: SubtitleTrack[]
  duration: number
  /** Avance del build, 0 a 1. null cuando ya termino o no hay sesion. */
  buildProgress: number | null
  /** Mensaje ya traducido de un fallo de la API. */
  apiError: string | null
  /** Slug del fallo, que es el contrato estable: va como codigo en el dialogo. */
  apiErrorCode: string | null
}

const ALWAYS_SUBS_KEY = 'alwaysSubs'
const CUE_SCALE_KEY = 'cueScale'

export function Player({
  masterUrl,
  title,
  subtitles,
  duration,
  buildProgress,
  apiError,
  apiErrorCode,
}: Props) {
  const windowRef = useRef<HTMLDivElement>(null)
  const screenRef = useRef<HTMLDivElement>(null)
  const videoRef = useRef<HTMLVideoElement>(null)
  const player = useHlsPlayer(masterUrl, videoRef)
  const video = useVideoState(videoRef, windowRef, duration)

  // "auto" es no haber elegido todavia: recien ahi manda la preferencia de
  // prender subtitulos sola. Cualquier eleccion explicita la reemplaza y no
  // se vuelve atras, ni siquiera cuando aparece una pista nueva.
  const [subChoice, setSubChoice] = useState<number | null | 'auto'>('auto')
  // Ultimo idioma elegido, para que el atajo de teclado pueda devolverlo.
  const lastSubRef = useRef<number | null>(null)
  const [openMenu, setOpenMenu] = useState<OpenMenu>(null)
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [subDelay, setSubDelay] = useState(0)
  const [alwaysSubs, setAlwaysSubs] = useState(() => readBool(ALWAYS_SUBS_KEY))
  const [cueScale, setCueScale] = useState(() =>
    readNumber(CUE_SCALE_KEY, 1, MIN_SIZE, MAX_SIZE),
  )

  const { basePx, cuePx } = useCueFontSize(screenRef, cueScale)

  const readySubs = useMemo(() => subtitles.filter((t) => t.url), [subtitles])
  const currentSub =
    subChoice === 'auto'
      ? alwaysSubs
        ? (readySubs[0]?.index ?? null)
        : null
      : subChoice
  const subUrl = useMemo(
    () => subtitles.find((t) => t.index === currentSub)?.url ?? null,
    [subtitles, currentSub],
  )

  // El chrome solo se esconde en pantalla completa: en ventana es el marco.
  const pinned =
    !video.fullscreen || !video.playing || openMenu !== null || settingsOpen
  const { visible, wake } = useAutoHide(pinned)

  // React renderiza el <track> pero no lo activa: textTrack.mode es imperativo.
  useEffect(() => {
    const el = videoRef.current
    if (!el) return
    const tracks = el.textTracks
    for (let i = 0; i < tracks.length; i++) {
      tracks[i].mode = subUrl ? 'showing' : 'disabled'
    }
  }, [subUrl])

  useSubtitleDelay(videoRef, subUrl, subDelay)

  useEffect(() => {
    writePref(ALWAYS_SUBS_KEY, alwaysSubs ? '1' : '0')
  }, [alwaysSubs])

  useEffect(() => {
    writePref(CUE_SCALE_KEY, String(cueScale))
  }, [cueScale])

  const selectSub = useCallback((index: number | null) => {
    setSubChoice(index)
    if (index !== null) lastSubRef.current = index
  }, [])

  const toggleCc = useCallback(() => {
    if (currentSub !== null) {
      setSubChoice(null)
      return
    }
    const fallback = readySubs[0]?.index ?? null
    const remembered = lastSubRef.current
    const restore =
      remembered !== null && readySubs.some((t) => t.index === remembered)
        ? remembered
        : fallback
    setSubChoice(restore)
  }, [currentSub, readySubs])

  const stop = useCallback(() => {
    const el = videoRef.current
    if (!el) return
    el.pause()
    video.seek(0)
  }, [video])

  const skip = useCallback(
    (seconds: number) => {
      video.seek(
        Math.max(0, Math.min(video.duration, video.currentTime + seconds)),
      )
    },
    [video],
  )

  // El handler necesita el estado ultimo, pero video cambia en cada timeupdate
  // (4 por segundo). Por ref, para no resuscribir el listener cada vez.
  const latest = useRef({ video, toggleCc })
  useEffect(() => {
    latest.current = { video, toggleCc }
  })

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null
      if (target && ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)) return

      const { video: v, toggleCc: cc } = latest.current

      switch (event.key) {
        case ' ':
        case 'k':
          event.preventDefault()
          v.toggle()
          break
        case 'ArrowLeft':
          v.seek(Math.max(0, v.currentTime - 10))
          break
        case 'ArrowRight':
          v.seek(Math.min(v.duration, v.currentTime + 10))
          break
        case 'f':
          v.toggleFullscreen()
          break
        case 'm':
          v.toggleMute()
          break
        case 'c':
          cc()
          break
        case 'Escape':
          setOpenMenu(null)
          setSettingsOpen(false)
          break
        default:
          return
      }
      wake()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [wake])

  const applySettings = useCallback(
    (settings: SubtitleSettings) => {
      selectSub(settings.sub)
      setSubDelay(settings.delay)
      setAlwaysSubs(settings.alwaysOn)
      setCueScale(settings.size)
      if (settings.audio !== player.currentAudio) player.selectAudio(settings.audio)
    },
    [player, selectSub],
  )

  const building = buildProgress !== null && buildProgress < 1

  const subtitleItems: MenuItem[] = subtitles.map((track) => ({
    key: `sub-${track.index}`,
    label: subtitleLabel(track),
    // Una pista sin url todavia se esta extrayendo: se muestra deshabilitada,
    // no se oculta, para que la lista no salte.
    detail: track.url ? undefined : 'generando…',
    checked: currentSub === track.index,
    disabled: !track.url,
    onSelect: () => {
      selectSub(track.index)
      setOpenMenu(null)
    },
  }))

  const subtitleExtras: MenuItem[] = [
    {
      key: 'sub-off',
      label: 'Desactivados',
      checked: currentSub === null,
      onSelect: () => {
        selectSub(null)
        setOpenMenu(null)
      },
    },
    {
      key: 'sub-settings',
      label: 'Ajustes de subtitulos…',
      onSelect: () => {
        setOpenMenu(null)
        setSettingsOpen(true)
      },
    },
  ]

  const audioItems: MenuItem[] = player.audioTracks.map((track) => ({
    key: `audio-${track.id}`,
    label: track.label,
    detail: channelLabel(track.channels) ?? undefined,
    checked: player.currentAudio === track.id,
    onSelect: () => {
      // Cambiar de idioma es esto y nada mas: no se toca el servidor.
      player.selectAudio(track.id)
      setOpenMenu(null)
    },
  }))

  const quality = player.height ? `${player.height}p` : null
  const channels = channelLabel(
    player.audioTracks.find((t) => t.id === player.currentAudio)?.channels,
  )

  let dialog = null
  let state: string
  if (apiError) {
    dialog = (
      <PlaybackError
        title="Homeflix Classic"
        message={apiError}
        code={apiErrorCode}
      />
    )
    state = 'Error — no se pudo abrir el video'
  } else if (player.fatal) {
    dialog = (
      <PlaybackError
        title="Homeflix Classic"
        message="Se perdio la conexion con el servidor de streaming. La posicion quedo guardada."
        code={player.fatalDetail}
        onRetry={player.reload}
      />
    )
    state = 'Error — se corto la reproduccion'
  } else if (!masterUrl) {
    const label = building ? 'Procesando' : 'Preparando'
    dialog = (
      <BuildProgress
        title={title}
        label={label}
        detail={
          building
            ? `${Math.round((buildProgress ?? 0) * 100)}% del video generado`
            : 'Esperando los primeros segmentos del servidor.'
        }
        progress={building ? buildProgress : null}
      />
    )
    state = building
      ? `Procesando — ${Math.round((buildProgress ?? 0) * 100)}%`
      : 'Preparando el video'
  } else if (video.buffering) {
    dialog = (
      <BuildProgress
        title={title}
        label="Cargando"
        detail="Rellenando el buffer de reproduccion."
        progress={null}
      />
    )
    state = 'Cargando…'
  } else if (video.playing) {
    state = title ? `Reproduciendo — ${title}` : 'Reproduciendo'
  } else {
    state = title ? `Pausado — ${title}` : 'Pausado'
  }

  return (
    <div
      ref={windowRef}
      className="window"
      data-fullscreen={video.fullscreen}
      data-idle={!visible}
      onMouseMove={wake}
      onClick={() => setOpenMenu(null)}
    >
      <TitleBar
        title={title}
        fullscreen={video.fullscreen}
        visible={visible}
        onToggleFullscreen={video.toggleFullscreen}
      />

      {/* El tamaño de los cues no se puede pasar por variable CSS: ::cue vive
          en el shadow tree del <video>. Va como regla propia, mas especifica
          que la de theme.css para ganarle sin depender del orden de carga. */}
      <style>{`.window .screen video::cue { font-size: ${cuePx}px; }`}</style>

      <div className="workspace">
        <div className="screen" ref={screenRef} data-blank={!masterUrl}>
          <video
            ref={videoRef}
            playsInline
            onClick={(e) => {
              e.stopPropagation()
              setOpenMenu(null)
              if (masterUrl) video.toggle()
            }}
          >
            {/* key: React tiene que reemplazar el elemento, no mutarle el src. */}
            {subUrl && (
              <track
                key={subUrl}
                kind="subtitles"
                src={subUrl}
                srcLang={
                  subtitles.find((t) => t.index === currentSub)?.language ?? 'und'
                }
                label={
                  subtitles.find((t) => t.index === currentSub)?.title ??
                  'Subtitulos'
                }
                default
              />
            )}
          </video>

          {masterUrl && !video.playing && !video.buffering && !dialog && (
            <button
              className="center-play"
              type="button"
              aria-label="Reproducir"
              onClick={(e) => {
                e.stopPropagation()
                video.toggle()
              }}
            >
              <span>
                <PlayIcon size={24} />
              </span>
            </button>
          )}
        </div>

        <ControlBar
          visible={visible}
          playing={video.playing}
          ready={masterUrl !== null}
          currentTime={video.currentTime}
          duration={video.duration}
          built={player.complete ? 1 : (buildProgress ?? 1)}
          volume={video.volume}
          muted={video.muted}
          quality={quality}
          fullscreen={video.fullscreen}
          subtitleItems={subtitleItems}
          subtitleExtras={subtitleExtras}
          audioItems={audioItems}
          openMenu={openMenu}
          onOpenMenu={setOpenMenu}
          onToggle={video.toggle}
          onStop={stop}
          onSkip={skip}
          onSeek={video.seek}
          onVolume={video.setVolume}
          onToggleMute={video.toggleMute}
          onToggleFullscreen={video.toggleFullscreen}
        />
      </div>

      <StatusBar
        state={state}
        format={[quality, channels].filter(Boolean).join(' · ') || '—'}
        duration={formatTime(video.duration)}
      />

      {dialog}

      {settingsOpen && (
        <div onClick={(e) => e.stopPropagation()}>
          <SettingsDialog
            subtitles={subtitles}
            audioTracks={player.audioTracks}
            basePx={basePx}
            initial={{
              sub: currentSub,
              audio: player.currentAudio,
              delay: subDelay,
              alwaysOn: alwaysSubs,
              size: cueScale,
            }}
            onApply={applySettings}
            onClose={() => setSettingsOpen(false)}
          />
        </div>
      )}
    </div>
  )
}
