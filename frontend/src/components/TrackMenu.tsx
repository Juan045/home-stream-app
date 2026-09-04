import type { SubtitleTrack } from '../api/client'
import type { HlsAudioTrack } from '../hooks/useHlsPlayer'

interface Props {
  audioTracks: HlsAudioTrack[]
  currentAudio: number
  onSelectAudio: (id: number) => void
  subtitles: SubtitleTrack[]
  currentSub: number | null
  onSelectSub: (index: number | null) => void
}

function subtitleLabel(track: SubtitleTrack): string {
  return track.title || track.language || `Pista ${track.index + 1}`
}

export function TrackMenu({
  audioTracks,
  currentAudio,
  onSelectAudio,
  subtitles,
  currentSub,
  onSelectSub,
}: Props) {
  return (
    <div className="panel" role="group" aria-label="Audio y subtitulos">
      <div className="col">
        <h2 id="menu-subs">Subtitulos</h2>
        {subtitles.length === 0 ? (
          <p className="empty">Este video no trae subtitulos.</p>
        ) : (
          <div className="pills" role="group" aria-labelledby="menu-subs">
            {subtitles.map((track) => (
              <button
                key={track.index}
                className="pill"
                type="button"
                // Una pista sin url todavia se esta extrayendo: se muestra
                // deshabilitada, no se oculta, para que la lista no salte.
                disabled={!track.url}
                data-on={currentSub === track.index}
                aria-pressed={currentSub === track.index}
                onClick={() => onSelectSub(track.index)}
              >
                {subtitleLabel(track)}
              </button>
            ))}
            <button
              className="pill"
              type="button"
              data-on={currentSub === null}
              aria-pressed={currentSub === null}
              onClick={() => onSelectSub(null)}
            >
              Desactivados
            </button>
          </div>
        )}
      </div>

      <div className="divider" />

      <div className="col">
        <h2 id="menu-audio">Audio</h2>
        {audioTracks.length === 0 ? (
          <p className="empty">Una sola pista de audio.</p>
        ) : (
          <div className="pills" role="group" aria-labelledby="menu-audio">
            {audioTracks.map((track) => (
              <button
                key={track.id}
                className="pill"
                type="button"
                data-on={currentAudio === track.id}
                aria-pressed={currentAudio === track.id}
                onClick={() => onSelectAudio(track.id)}
              >
                {track.label}
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
