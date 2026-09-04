import type { MouseEvent, KeyboardEvent } from 'react'
import { formatTime } from '../format'
import { FullscreenIcon, MenuIcon, PauseIcon, PlayIcon, VolumeIcon } from './icons'

interface Props {
  visible: boolean
  playing: boolean
  currentTime: number
  duration: number
  /** Fraccion del video ya generada. En modo EVENT no se puede buscar mas alla. */
  built: number
  volume: number
  muted: boolean
  ccOn: boolean
  ccAvailable: boolean
  menuOpen: boolean
  fullscreen: boolean
  onToggle: () => void
  onSeek: (seconds: number) => void
  onVolume: (v: number) => void
  onToggleMute: () => void
  onToggleCc: () => void
  onToggleMenu: () => void
  onToggleFullscreen: () => void
}

export function ControlBar(props: Props) {
  const {
    visible,
    playing,
    currentTime,
    duration,
    built,
    volume,
    muted,
    ccOn,
    ccAvailable,
    menuOpen,
    fullscreen,
    onToggle,
    onSeek,
    onVolume,
    onToggleMute,
    onToggleCc,
    onToggleMenu,
    onToggleFullscreen,
  } = props

  const ratio = duration > 0 ? Math.min(currentTime / duration, 1) : 0

  const seekFromPointer = (event: MouseEvent<HTMLDivElement>) => {
    if (duration <= 0) return
    const rect = event.currentTarget.getBoundingClientRect()
    const position = (event.clientX - rect.left) / rect.width
    onSeek(Math.max(0, Math.min(position, 1)) * duration)
  }

  const seekFromKeyboard = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'ArrowLeft') {
      event.preventDefault()
      onSeek(Math.max(0, currentTime - 10))
    } else if (event.key === 'ArrowRight') {
      event.preventDefault()
      onSeek(Math.min(duration, currentTime + 10))
    }
  }

  return (
    <div className="capsule" data-visible={visible}>
      <button
        className="play"
        type="button"
        onClick={onToggle}
        aria-label={playing ? 'Pausar' : 'Reproducir'}
      >
        {playing ? (
          <PauseIcon color="#241600" />
        ) : (
          <PlayIcon size={14} color="#241600" />
        )}
      </button>

      <div className="scrub">
        <span className="time now">{formatTime(currentTime)}</span>
        <div
          className="track"
          role="slider"
          tabIndex={0}
          aria-label="Posicion"
          aria-valuemin={0}
          aria-valuemax={Math.round(duration)}
          aria-valuenow={Math.round(currentTime)}
          aria-valuetext={formatTime(currentTime)}
          onClick={seekFromPointer}
          onKeyDown={seekFromKeyboard}
        >
          <div className="rail">
            <div className="built" style={{ width: `${built * 100}%` }} />
            <div className="fill" style={{ width: `${ratio * 100}%` }} />
          </div>
          <div className="thumb" style={{ left: `${ratio * 100}%` }} />
        </div>
        <span className="time total">{formatTime(duration)}</span>
      </div>

      <div className="cluster">
        <div className="volume">
          <button
            className="icon-btn"
            type="button"
            onClick={onToggleMute}
            aria-label={muted ? 'Activar sonido' : 'Silenciar'}
          >
            <VolumeIcon muted={muted || volume === 0} />
          </button>
          <input
            type="range"
            min={0}
            max={1}
            step={0.01}
            value={muted ? 0 : volume}
            aria-label="Volumen"
            onChange={(e) => onVolume(Number(e.target.value))}
          />
        </div>

        <button
          className="cc"
          type="button"
          data-on={ccOn}
          disabled={!ccAvailable}
          onClick={onToggleCc}
          aria-pressed={ccOn}
          aria-label="Subtitulos"
        >
          CC
        </button>

        <button
          className="icon-btn"
          type="button"
          data-on={menuOpen}
          onClick={onToggleMenu}
          aria-expanded={menuOpen}
          aria-label="Audio y subtitulos"
        >
          <MenuIcon />
        </button>

        <button
          className="icon-btn"
          type="button"
          onClick={onToggleFullscreen}
          aria-label={fullscreen ? 'Salir de pantalla completa' : 'Pantalla completa'}
        >
          <FullscreenIcon exit={fullscreen} />
        </button>
      </div>
    </div>
  )
}
