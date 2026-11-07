/**
 * Ajuste de codificacion (/settings/): el codec en vivo y el de biblioteca.
 *
 * Carga con `GET /settings/encoding` y guarda con `PUT`. Las opciones salen de
 * la respuesta, no de una lista propia, y despues de guardar se repinta con lo
 * que devolvio el servidor.
 */
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { readEncodingSettings, updateEncodingSettings, type EncodingSettings } from '../../api/client'
import { Icon } from '../../components/Icon'
import { CodecCard } from './CodecCard'
import { AdminShell, ServerAlert } from './admin'
import { toFailure, type Failure } from './media'

const LIVE_TEXTS: Record<string, string> = {
  h264: '720p. Arranca rápido y lo reproduce cualquier navegador.',
  av1: '720p, 10 bits. Puede cortarse si la CPU no llega a tiempo real.',
}

const ARCHIVE_TEXTS: Record<string, string> = {
  h264: '1080p. Compatible y más pesado.',
  av1: '1080p, 10 bits. Más liviano; tarda horas.',
}

// Sin MediaSource no se puede saber (iOS): se avisa solo ante un "no" seguro.
const CANNOT_PLAY_AV1 =
  typeof MediaSource !== 'undefined' && !MediaSource.isTypeSupported('video/mp4; codecs="av01.0.08M.10"')

interface Draft {
  live: string
  archive: string
}

const draftFrom = (settings: EncodingSettings): Draft => ({ live: settings.live_codec, archive: settings.archive_codec })

/** El mensaje compartido de `invalid_request` habla de remux, que aca no aplica. */
function saveFailure(err: unknown): Failure {
  const failure = toFailure(err)
  return failure.slug === 'invalid_request' ? { ...failure, msg: 'Ese códec no está disponible. Recargá la página.' } : failure
}

export default function EncodingSettingsPage() {
  // Lo que dijo el servidor, y lo que se esta eligiendo. Comparandolos sale si hay cambios.
  const [settings, setSettings] = useState<EncodingSettings | null>(null)
  const [draft, setDraft] = useState<Draft | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<Failure | null>(null)
  const [toast, setToast] = useState(false)
  const toastTimer = useRef(0)

  useEffect(() => {
    const controller = new AbortController()
    void readEncodingSettings(controller.signal)
      .then((found) => {
        setSettings(found)
        setDraft(draftFrom(found))
      })
      .catch((err: unknown) => {
        if (!controller.signal.aborted) setError(toFailure(err))
      })
    return () => controller.abort()
  }, [])

  async function save(event: FormEvent) {
    event.preventDefault()
    if (draft === null || busy) return

    setBusy(true)
    setError(null)
    try {
      const saved = await updateEncodingSettings(draft.live, draft.archive)
      setSettings(saved)
      setDraft(draftFrom(saved))
      setToast(true)
      clearTimeout(toastTimer.current)
      toastTimer.current = window.setTimeout(() => setToast(false), 2400)
    } catch (err: unknown) {
      setError(saveFailure(err))
      window.scrollTo({ top: 0, behavior: 'smooth' })
    } finally {
      setBusy(false)
    }
  }

  const dirty = settings !== null && draft !== null && (draft.live !== settings.live_codec || draft.archive !== settings.archive_codec)

  return (
    <>
      <AdminShell
        crumb="Codificación"
        heading="Codificación"
        lead="Aplica a lo que todavía no se generó. Los títulos ya procesados conservan su códec."
        encoding
      >
        <div className="form-view">
          {error !== null && <ServerAlert failure={error} />}

          {settings !== null && draft !== null && (
            <form noValidate onSubmit={(event) => void save(event)}>
              <CodecCard
                name="live-codec"
                icon="play"
                title="En vivo"
                subtitle="Al tocar Play."
                options={settings.live_codecs}
                value={draft.live}
                defaultValue={settings.default_live_codec}
                texts={LIVE_TEXTS}
                disabled={busy}
                onPick={(live) => setDraft({ ...draft, live })}
              >
                <p className="desc">Lo que se reempaqueta sin recodificar no usa este ajuste.</p>
                {draft.live === 'av1' && (
                  <p className="desc"><Icon name="alert" />Si la reproducción se corta, el servidor no llega a codificar en tiempo real.</p>
                )}
                {draft.live === 'av1' && CANNOT_PLAY_AV1 && (
                  <p className="desc"><Icon name="alert" />Este navegador no reproduce AV1.</p>
                )}
              </CodecCard>

              <CodecCard
                name="archive-codec"
                icon="layers"
                title="Biblioteca"
                subtitle="Al codificar a pedido."
                options={settings.archive_codecs}
                value={draft.archive}
                defaultValue={settings.default_archive_codec}
                texts={ARCHIVE_TEXTS}
                disabled={busy}
                onPick={(archive) => setDraft({ ...draft, archive })}
              >
                {draft.archive === 'av1' && CANNOT_PLAY_AV1 && (
                  <p className="desc"><Icon name="alert" />Este navegador no reproduce AV1.</p>
                )}
              </CodecCard>

              <div className="actionbar">
                <span className={!busy && dirty ? 'grow dirty' : 'grow'}>
                  <span className="dot" />
                  <span>{busy ? 'Guardando…' : dirty ? 'Cambios sin guardar' : 'Sin cambios'}</span>
                </span>
                {/* Solo cambia el borrador: nada se guarda hasta tocar Guardar. */}
                <button
                  className="btn btn-text sl"
                  type="button"
                  onClick={() => setDraft({ live: settings.default_live_codec, archive: settings.default_archive_codec })}
                  disabled={busy}
                >
                  Restaurar predeterminados
                </button>
                <button className="btn btn-filled" type="submit" disabled={busy || !dirty}>
                  {busy ? <><span className="spin" />Guardando</> : <><Icon name="ticket-check" />Guardar</>}
                </button>
              </div>
            </form>
          )}
        </div>
      </AdminShell>

      <div className={toast ? 'toast show' : 'toast'} role="status">
        <Icon name="check2" /><span>{toast ? 'Ajuste guardado' : ''}</span>
      </div>
    </>
  )
}
