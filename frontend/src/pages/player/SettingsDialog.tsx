/**
 * El dialogo avanzado del artboard (Subtitles ▸ Subtitle settings…).
 *
 * Sigue el idioma modal de XP: los cambios se editan sobre un borrador y no
 * tocan la reproduccion hasta Aplicar o Aceptar. Cancelar los descarta.
 */
import { useState } from 'react'
import type { SubtitleTrack } from '../../api/client'
import type { HlsAudioTrack } from './hooks/useHlsPlayer'
import { channelLabel, formatDelay, subtitleLabel } from '../../format'
import { Dialog } from '../../components/Dialog'
import { Slider } from '../../components/Slider'

export interface SubtitleSettings {
  sub: number | null
  audio: number
  /** Corrimiento de los subtitulos en segundos. Positivo: aparecen despues. */
  delay: number
  /** Prender la primera pista disponible al abrir un video. */
  alwaysOn: boolean
  /** Escala del texto: 1 es el tamaño que el player calcula por si mismo. */
  size: number
}

interface Props {
  subtitles: SubtitleTrack[]
  audioTracks: HlsAudioTrack[]
  /** Tamaño en pixeles al 100%, para que la muestra sea fiel. */
  basePx: number
  initial: SubtitleSettings
  onApply: (settings: SubtitleSettings) => void
  onClose: () => void
}

const STEP = 0.1

export const MIN_SIZE = 0.5
export const MAX_SIZE = 2
const SIZE_STEP = 0.05

/** Redondea al escalon mas cercano para que no queden valores como 117%. */
const snap = (value: number) =>
  Math.min(MAX_SIZE, Math.max(MIN_SIZE, Math.round(value / SIZE_STEP) * SIZE_STEP))

export function SettingsDialog({
  subtitles,
  audioTracks,
  basePx,
  initial,
  onApply,
  onClose,
}: Props) {
  const [tab, setTab] = useState<'subs' | 'audio' | 'style'>('subs')
  const [draft, setDraft] = useState<SubtitleSettings>(initial)

  const patch = (fields: Partial<SubtitleSettings>) =>
    setDraft((current) => ({ ...current, ...fields }))

  const accept = () => {
    onApply(draft)
    onClose()
  }

  return (
    <Dialog title="Subtitulos y audio" onClose={onClose} wide>
      <div className="tabs" role="tablist">
        <button
          className="tab"
          type="button"
          role="tab"
          aria-selected={tab === 'subs'}
          data-on={tab === 'subs'}
          onClick={() => setTab('subs')}
        >
          Subtitulos
        </button>
        <button
          className="tab"
          type="button"
          role="tab"
          aria-selected={tab === 'audio'}
          data-on={tab === 'audio'}
          onClick={() => setTab('audio')}
        >
          Audio
        </button>
        <button
          className="tab"
          type="button"
          role="tab"
          aria-selected={tab === 'style'}
          data-on={tab === 'style'}
          onClick={() => setTab('style')}
        >
          Estilo
        </button>
      </div>

      {tab === 'subs' && (
        <div className="sheet" role="tabpanel" aria-label="Subtitulos">
          <div className="field-row">
            <span className="caption">Pista de subtitulos:</span>
            <div className="listbox" role="listbox" aria-label="Pista de subtitulos">
              {subtitles.map((track) => (
                <button
                  key={track.index}
                  className="option"
                  type="button"
                  role="option"
                  aria-selected={draft.sub === track.index}
                  data-on={draft.sub === track.index}
                  // Sin url la pista todavia se esta extrayendo.
                  disabled={!track.url}
                  onClick={() => patch({ sub: track.index })}
                >
                  <span>{subtitleLabel(track)}</span>
                  {!track.url && <span className="detail">generando…</span>}
                </button>
              ))}
              <button
                className="option"
                type="button"
                role="option"
                aria-selected={draft.sub === null}
                data-on={draft.sub === null}
                onClick={() => patch({ sub: null })}
              >
                <span>Desactivados</span>
              </button>
            </div>
          </div>

          <div className="inline">
            <span className="caption">Retardo:</span>
            <div className="spin-value" aria-live="off">
              {formatDelay(draft.delay)}
            </div>
            <div className="spinner">
              <button
                type="button"
                aria-label="Aumentar el retardo"
                onClick={() => patch({ delay: draft.delay + STEP })}
              >
                <svg width="7" height="4" viewBox="0 0 7 4" aria-hidden="true">
                  <path d="M3.5 0 L7 4 L0 4 Z" fill="currentColor" />
                </svg>
              </button>
              <button
                type="button"
                aria-label="Reducir el retardo"
                onClick={() => patch({ delay: draft.delay - STEP })}
              >
                <svg width="7" height="4" viewBox="0 0 7 4" aria-hidden="true">
                  <path d="M0 0 L7 0 L3.5 4 Z" fill="currentColor" />
                </svg>
              </button>
            </div>
            <button
              className="dlg-btn"
              type="button"
              onClick={() => patch({ delay: 0 })}
            >
              A cero
            </button>
          </div>

          <button
            className="check"
            type="button"
            role="checkbox"
            aria-checked={draft.alwaysOn}
            onClick={() => patch({ alwaysOn: !draft.alwaysOn })}
          >
            <span className="box" aria-hidden="true">
              {draft.alwaysOn ? '✓' : ''}
            </span>
            Mostrar subtitulos siempre que haya una pista disponible
          </button>
        </div>
      )}

      {tab === 'audio' && (
        <div className="sheet" role="tabpanel" aria-label="Audio">
          <div className="field-row">
            <span className="caption">Pista de audio:</span>
            {audioTracks.length === 0 ? (
              <p className="text">Este video trae una sola pista de audio.</p>
            ) : (
              <div className="listbox" role="listbox" aria-label="Pista de audio">
                {audioTracks.map((track) => (
                  <button
                    key={track.id}
                    className="option"
                    type="button"
                    role="option"
                    aria-selected={draft.audio === track.id}
                    data-on={draft.audio === track.id}
                    onClick={() => patch({ audio: track.id })}
                  >
                    <span>{track.label}</span>
                    {channelLabel(track.channels) && (
                      <span className="detail">{channelLabel(track.channels)}</span>
                    )}
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>
      )}

      {tab === 'style' && (
        <div className="sheet" role="tabpanel" aria-label="Estilo">
          <div className="inline">
            <span className="caption">Tamaño:</span>
            <Slider
              kind="seek"
              label="Tamaño de los subtitulos"
              value={(draft.size - MIN_SIZE) / (MAX_SIZE - MIN_SIZE)}
              step={SIZE_STEP / (MAX_SIZE - MIN_SIZE)}
              valueText={`${Math.round(draft.size * 100)} por ciento`}
              live
              onChange={(ratio) =>
                patch({ size: snap(MIN_SIZE + ratio * (MAX_SIZE - MIN_SIZE)) })
              }
            />
            <div className="spin-value">{Math.round(draft.size * 100)}%</div>
            <button
              className="dlg-btn"
              type="button"
              onClick={() => patch({ size: 1 })}
            >
              A 100%
            </button>
          </div>

          <div className="field-row">
            <span className="caption">Muestra:</span>
            <div className="cue-preview" aria-hidden="true">
              <span style={{ fontSize: `${Math.round(basePx * draft.size)}px` }}>
                — En primavera, no antes.
              </span>
            </div>
          </div>

          <p className="text">
            El tamaño es relativo a la pantalla: al pasar a pantalla completa el
            texto acompaña, y esta escala se aplica sobre eso.
          </p>
        </div>
      )}

      <div className="buttons">
        <button className="dlg-btn default" type="button" onClick={accept}>
          Aceptar
        </button>
        <button className="dlg-btn" type="button" onClick={onClose}>
          Cancelar
        </button>
        <button className="dlg-btn" type="button" onClick={() => onApply(draft)}>
          Aplicar
        </button>
      </div>
    </Dialog>
  )
}
