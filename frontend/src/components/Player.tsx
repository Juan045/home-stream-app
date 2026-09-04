import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { SubtitleTrack } from '../api/client'
import { useAutoHide } from '../hooks/useAutoHide'
import { useHlsPlayer } from '../hooks/useHlsPlayer'
import { useVideoState } from '../hooks/useVideoState'
import { BuildProgress } from './BuildProgress'
import { ControlBar } from './ControlBar'
import { PlaybackError } from './PlaybackError'
import { TrackMenu } from './TrackMenu'
import { PlayIcon } from './icons'

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
}

export function Player({
  masterUrl,
  title,
  subtitles,
  duration,
  buildProgress,
  apiError,
}: Props) {
  const containerRef = useRef<HTMLDivElement>(null)
  const videoRef = useRef<HTMLVideoElement>(null)
  const player = useHlsPlayer(masterUrl, videoRef)
  const video = useVideoState(videoRef, containerRef, duration)

  const [currentSub, setCurrentSub] = useState<number | null>(null)
  // Ultimo idioma elegido, para que el boton CC pueda devolverlo.
  const lastSubRef = useRef<number | null>(null)
  const [menuOpen, setMenuOpen] = useState(false)

  const readySubs = useMemo(() => subtitles.filter((t) => t.url), [subtitles])
  const subUrl = useMemo(
    () => subtitles.find((t) => t.index === currentSub)?.url ?? null,
    [subtitles, currentSub],
  )

  // La capsula se queda quieta mientras haya pausa o panel abierto.
  const { visible, wake } = useAutoHide(!video.playing || menuOpen)

  // React renderiza el <track> pero no lo activa: textTrack.mode es imperativo.
  useEffect(() => {
    const el = videoRef.current
    if (!el) return
    const tracks = el.textTracks
    for (let i = 0; i < tracks.length; i++) {
      tracks[i].mode = subUrl ? 'showing' : 'disabled'
    }
  }, [subUrl])

  const selectSub = useCallback((index: number | null) => {
    setCurrentSub(index)
    if (index !== null) lastSubRef.current = index
  }, [])

  const toggleCc = useCallback(() => {
    if (currentSub !== null) {
      setCurrentSub(null)
      return
    }
    const fallback = readySubs[0]?.index ?? null
    const remembered = lastSubRef.current
    const restore =
      remembered !== null && readySubs.some((t) => t.index === remembered)
        ? remembered
        : fallback
    setCurrentSub(restore)
  }, [currentSub, readySubs])

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
          setMenuOpen(false)
          break
        default:
          return
      }
      wake()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [wake])

  const building = buildProgress !== null && buildProgress < 1
  const idle = !visible && video.playing && !menuOpen

  let overlay = null
  if (apiError) {
    overlay = <PlaybackError title="No se pudo abrir el video" message={apiError} />
  } else if (player.fatal) {
    overlay = (
      <PlaybackError
        title="Se corto la reproduccion"
        message="Se perdio la conexion con el servidor de streaming."
        onRetry={player.reload}
      />
    )
  } else if (!masterUrl) {
    overlay = (
      <BuildProgress
        progress={buildProgress}
        label={building ? 'Procesando' : 'Preparando'}
      />
    )
  } else if (video.buffering) {
    overlay = <BuildProgress progress={null} label="Cargando" />
  }

  return (
    <div
      ref={containerRef}
      className="stage"
      data-idle={idle}
      onMouseMove={wake}
      onClick={() => setMenuOpen(false)}
    >
      <video
        ref={videoRef}
        playsInline
        onClick={(e) => {
          e.stopPropagation()
          setMenuOpen(false)
          if (masterUrl) video.toggle()
        }}
      >
        {/* key: React tiene que reemplazar el elemento, no mutarle el src. */}
        {subUrl && (
          <track
            key={subUrl}
            kind="subtitles"
            src={subUrl}
            srcLang={subtitles.find((t) => t.index === currentSub)?.language ?? 'und'}
            label={subtitles.find((t) => t.index === currentSub)?.title ?? 'Subtitulos'}
            default
          />
        )}
      </video>

      <div className="scrim" data-visible={visible} />

      <div className="title" data-visible={visible}>
        {title}
      </div>

      {masterUrl && !video.playing && !video.buffering && !overlay && (
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
            <PlayIcon size={20} color="#fff6ea" />
          </span>
        </button>
      )}

      {overlay}

      {menuOpen && (
        <div onClick={(e) => e.stopPropagation()}>
          <TrackMenu
            audioTracks={player.audioTracks}
            currentAudio={player.currentAudio}
            onSelectAudio={player.selectAudio}
            subtitles={subtitles}
            currentSub={currentSub}
            onSelectSub={selectSub}
          />
        </div>
      )}

      <div onClick={(e) => e.stopPropagation()}>
        <ControlBar
          visible={visible && !overlay}
          playing={video.playing}
          currentTime={video.currentTime}
          duration={video.duration}
          built={player.complete ? 1 : (buildProgress ?? 1)}
          volume={video.volume}
          muted={video.muted}
          ccOn={currentSub !== null}
          ccAvailable={readySubs.length > 0}
          menuOpen={menuOpen}
          fullscreen={video.fullscreen}
          onToggle={video.toggle}
          onSeek={video.seek}
          onVolume={video.setVolume}
          onToggleMute={video.toggleMute}
          onToggleCc={toggleCc}
          onToggleMenu={() => setMenuOpen((open) => !open)}
          onToggleFullscreen={video.toggleFullscreen}
        />
      </div>
    </div>
  )
}
