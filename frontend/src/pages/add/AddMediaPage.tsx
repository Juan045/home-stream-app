/**
 * Alta de un archivo al catalogo: artboard "Alta de archivo" de Homeflix Form.
 *
 * Se entra solo por URL (/new/). La galeria todavia no tiene boton que lleve
 * aca, y eso es a proposito.
 *
 * Un campo y nada mas, porque `POST /media` recibe un solo dato: la ruta
 * relativa a MEDIA_ROOT. El titulo sale del nombre del archivo y los codecs,
 * la duracion y las pistas las dicta ffprobe en el servidor; todo eso aparece
 * en el panel de abajo cuando la ficha queda creada. Los campos editoriales se
 * corrigen despues con `PATCH /media/{id_media}`, que todavia no tiene vista.
 */
import { useState, type FormEvent } from 'react'
import {
  ApiError,
  createMedia,
  errorMessage,
  type MediaResponse,
} from '../../api/client'
import { StatusBar } from '../../components/StatusBar'
import { TitleBar } from '../../components/TitleBar'
import { MISSING, formatRuntime, mediaFacts } from '../../format'
import './add.css'

export default function AddMediaPage() {
  const [path, setPath] = useState('')
  const [busy, setBusy] = useState(false)
  const [media, setMedia] = useState<MediaResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  const target = path.trim()

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (target === '' || busy) return

    setBusy(true)
    setError(null)
    setMedia(null)
    try {
      setMedia(await createMedia(target))
    } catch (err: unknown) {
      setError(err instanceof ApiError ? failure(err) : errorMessage('unknown'))
    } finally {
      setBusy(false)
    }
  }

  function reset() {
    setPath('')
    setMedia(null)
    setError(null)
  }

  return (
    <div className="window addmedia">
      <TitleBar title="Homeflix — Add file to library" />

      <form className="paper" onSubmit={(event) => void submit(event)}>
        <div className="stack">
          <div className="group">
            <div className="head">
              <span>Source file</span>
              <span className="hint">Relativa a MEDIA_ROOT</span>
            </div>
            <div className="body">
              <div className="row">
                <label htmlFor="file-path">File</label>
                <input
                  id="file-path"
                  type="text"
                  value={path}
                  onChange={(event) => setPath(event.target.value)}
                  placeholder="films/Dune (2021).mkv"
                  spellCheck={false}
                  autoComplete="off"
                  autoFocus
                  disabled={busy}
                />
              </div>
              <p className="note">
                La ruta cuelga del directorio de medios del servidor, sin el
                punto de montaje: <code>films/Dune (2021).mkv</code>, no{' '}
                <code>/media/films/Dune (2021).mkv</code>. Solo <code>.mp4</code>{' '}
                y <code>.mkv</code>.
              </p>
              {busy && (
                <div className="blocks" data-indeterminate="true" role="presentation">
                  <i />
                  <i />
                  <i />
                  <i />
                </div>
              )}
            </div>
          </div>

          {error !== null && (
            <div className="alert">
              <div className="notice">
                <div className="sign">!</div>
                <p className="text">{error}</p>
              </div>
            </div>
          )}

          {media !== null && (
            <div className="group">
              <div className="head">
                <span>Detected</span>
                <span className="hint">id {media.id_media}</span>
              </div>
              <div className="body">
                <div className="row">
                  <span className="label">Title</span>
                  <div className="readout">{media.title}</div>
                </div>
                {mediaFacts(media).map(([label, value]) => (
                  <div className="row" key={label}>
                    <span className="label">{label}</span>
                    <div className="readout" data-missing={value === MISSING}>
                      {value}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}

          <div className="buttons">
            {/* Un <a> y no un Link: cada vista es una pagina propia y no hay
                router del lado del cliente. */}
            <a className="dlg-btn" href="/">
              Back to library
            </a>
            <button className="dlg-btn" type="button" onClick={reset} disabled={busy}>
              {media === null ? 'Limpiar' : 'Cargar otro'}
            </button>
            <button
              className="dlg-btn default"
              type="submit"
              disabled={busy || target === ''}
            >
              Add to library
            </button>
          </div>
        </div>
      </form>

      <StatusBar
        state={state(busy, error, media)}
        format={
          media && media.width && media.height
            ? `${media.width}×${media.height}`
            : '—'
        }
        duration={media ? formatRuntime(media.duration) : 'Alta'}
      />
    </div>
  )
}

/**
 * `invalid_path` significa lo contrario en los dos endpoints que lo usan: el
 * player manda la ruta absoluta y el alta la relativa. El mensaje compartido
 * habla del caso del player, asi que aca se traduce.
 */
function failure(err: ApiError): string {
  return err.slug === 'invalid_path'
    ? 'La ruta tiene que ser relativa a MEDIA_ROOT.'
    : errorMessage(err.slug)
}

function state(
  busy: boolean,
  error: string | null,
  media: MediaResponse | null,
): string {
  if (busy) return 'Analizando el archivo…'
  if (error !== null) return 'Error'
  if (media !== null) return `Added · ${media.title}`
  return 'Ready to add'
}
