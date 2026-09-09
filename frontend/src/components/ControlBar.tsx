import { formatTime } from '../format'
import {
  BackIcon,
  ForwardIcon,
  FullscreenIcon,
  PauseIcon,
  PlayIcon,
  StopIcon,
  VolumeIcon,
} from './icons'
import { Slider } from './Slider'
import { TrackMenu, type MenuItem } from './TrackMenu'

export type OpenMenu = 'subs' | 'audio' | null

interface Props {
  visible: boolean
  playing: boolean
  /** Sin master no hay nada que controlar todavia. */
  ready: boolean
  currentTime: number
  duration: number
  /** Fraccion del video ya generada. En modo EVENT no se puede ir mas alla. */
  built: number
  volume: number
  muted: boolean
  /** "1080p", leido del RESOLUTION del master. null hasta que se parsea. */
  quality: string | null
  fullscreen: boolean
  subtitleItems: MenuItem[]
  subtitleExtras: MenuItem[]
  audioItems: MenuItem[]
  openMenu: OpenMenu
  onOpenMenu: (menu: OpenMenu) => void
  onToggle: () => void
  onStop: () => void
  onSkip: (seconds: number) => void
  onSeek: (seconds: number) => void
  onVolume: (v: number) => void
  onToggleMute: () => void
  onToggleFullscreen: () => void
}

export function ControlBar(props: Props) {
  const {
    visible,
    playing,
    ready,
    currentTime,
    duration,
    built,
    volume,
    muted,
    quality,
    fullscreen,
    subtitleItems,
    subtitleExtras,
    audioItems,
    openMenu,
    onOpenMenu,
    onToggle,
    onStop,
    onSkip,
    onSeek,
    onVolume,
    onToggleMute,
    onToggleFullscreen,
  } = props

  const ratio = duration > 0 ? Math.min(currentTime / duration, 1) : 0
  // Diez segundos de salto, en fraccion de la barra: el mismo paso que las
  // flechas del teclado.
  const seekStep = duration > 0 ? 10 / duration : 0

  const toggleMenu = (menu: Exclude<OpenMenu, null>) =>
    onOpenMenu(openMenu === menu ? null : menu)

  return (
    // Los clicks de la barra no llegan a la ventana, que es la que cierra los
    // menues al hacer click afuera.
    <div
      className="controls"
      data-chrome
      data-visible={visible}
      onClick={(event) => event.stopPropagation()}
    >
      <div className="row">
        <Slider
          kind="seek"
          label="Posicion"
          value={ratio}
          built={built}
          step={seekStep}
          disabled={!ready || duration <= 0}
          valueText={formatTime(currentTime)}
          onChange={(r) => onSeek(r * duration)}
        />
        <div className="timecode">
          {formatTime(currentTime)} / {formatTime(duration)}
        </div>
      </div>

      <div className="row split">
        <div className="group">
          <button
            className="btn square primary"
            type="button"
            disabled={!ready}
            onClick={onToggle}
            aria-label={playing ? 'Pausar' : 'Reproducir'}
          >
            {playing ? <PauseIcon /> : <PlayIcon />}
          </button>
          <button
            className="btn square"
            type="button"
            disabled={!ready}
            onClick={onStop}
            aria-label="Detener"
          >
            <StopIcon />
          </button>
          <button
            className="btn square"
            type="button"
            disabled={!ready}
            onClick={() => onSkip(-10)}
            aria-label="Retroceder 10 segundos"
          >
            <BackIcon />
          </button>
          <button
            className="btn square"
            type="button"
            disabled={!ready}
            onClick={() => onSkip(10)}
            aria-label="Adelantar 10 segundos"
          >
            <ForwardIcon />
          </button>

          <div className="sep" aria-hidden="true" />

          <button
            className="btn square"
            type="button"
            onClick={onToggleMute}
            aria-label={muted ? 'Activar sonido' : 'Silenciar'}
          >
            <VolumeIcon muted={muted || volume === 0} />
          </button>
          <Slider
            kind="level"
            label="Volumen"
            value={muted ? 0 : volume}
            step={0.05}
            live
            valueText={`${Math.round((muted ? 0 : volume) * 100)}%`}
            onChange={onVolume}
          />
        </div>

        <div className="group right">
          <div className="anchor">
            <button
              className="btn"
              type="button"
              data-open={openMenu === 'subs'}
              aria-haspopup="menu"
              aria-expanded={openMenu === 'subs'}
              onClick={() => toggleMenu('subs')}
            >
              Subtitles <span className="caret">{openMenu === 'subs' ? '▲' : '▼'}</span>
            </button>
            {openMenu === 'subs' && (
              <TrackMenu
                head="SUBTITLE TRACK"
                items={subtitleItems}
                extras={subtitleExtras}
              />
            )}
          </div>

          <div className="anchor">
            <button
              className="btn"
              type="button"
              disabled={audioItems.length === 0}
              data-open={openMenu === 'audio'}
              aria-haspopup="menu"
              aria-expanded={openMenu === 'audio'}
              onClick={() => toggleMenu('audio')}
            >
              Audio <span className="caret">{openMenu === 'audio' ? '▲' : '▼'}</span>
            </button>
            {openMenu === 'audio' && (
              <TrackMenu head="AUDIO TRACK" items={audioItems} />
            )}
          </div>

          {quality && <div className="badge">{quality}</div>}

          <button
            className="btn square"
            type="button"
            onClick={onToggleFullscreen}
            aria-label={fullscreen ? 'Salir de pantalla completa' : 'Pantalla completa'}
          >
            <FullscreenIcon exit={fullscreen} />
          </button>
        </div>
      </div>
    </div>
  )
}
