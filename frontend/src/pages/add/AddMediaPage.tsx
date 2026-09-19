/**
 * Alta de un archivo al catalogo: artboard "Alta de archivo" de Homeflix Form.
 *
 * Se entra solo por URL (/new/). La galeria todavia no tiene boton que lleve
 * aca, y eso es a proposito.
 *
 * Un campo y nada mas para el alta, porque `POST /media` recibe un solo dato: la
 * ruta relativa a MEDIA_ROOT. El titulo sale del nombre del archivo y los
 * codecs, la duracion y las pistas las dicta ffprobe en el servidor; todo eso
 * aparece en el panel de abajo cuando la ficha queda creada.
 *
 * Con la ficha creada aparece "Editar", que abre los campos editoriales sobre
 * ese mismo panel y los guarda con `PATCH /media/{id_media}`. Es el unico lugar
 * del frontend que edita una ficha.
 */
import { useState, type FormEvent } from 'react'
import {
  ApiError,
  createMedia,
  errorMessage,
  updateMedia,
  type MediaPatch,
  type MediaResponse,
} from '../../api/client'
import { StatusBar } from '../../components/StatusBar'
import { TitleBar } from '../../components/TitleBar'
import {
  MISSING,
  audioLabel,
  formatRuntime,
  mediaFacts,
  subtitleLabel,
} from '../../format'
import './add.css'

export default function AddMediaPage() {
  const [path, setPath] = useState('')
  const [busy, setBusy] = useState(false)
  const [media, setMedia] = useState<MediaResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  // Null cuando no se esta editando: el borrador nace del PATCH y muere con el.
  const [draft, setDraft] = useState<Draft | null>(null)

  const target = path.trim()
  const editing = draft !== null

  async function submit(event: FormEvent) {
    event.preventDefault()
    // El `editing` no es de mas: los campos de edicion viven dentro de este
    // mismo <form>, y sin esto un Enter mientras se edita reintentaria el alta
    // del mismo archivo, que ya esta en el catalogo (409).
    if (target === '' || busy || editing) return

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

  async function save() {
    if (media === null || draft === null || busy) return

    setBusy(true)
    setError(null)
    try {
      // Se repinta con la ficha que devolvio el servidor, no con el borrador.
      setMedia(await updateMedia(media.id_media, toPatch(draft)))
      setDraft(null)
    } catch (err: unknown) {
      setError(err instanceof ApiError ? failure(err) : errorMessage('unknown'))
    } finally {
      setBusy(false)
    }
  }

  function edit(fields: Partial<Draft>) {
    setDraft((current) => (current === null ? null : { ...current, ...fields }))
  }

  function reset() {
    setPath('')
    setMedia(null)
    setError(null)
    setDraft(null)
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
                <span>{draft === null ? 'Detected' : 'Editorial fields'}</span>
                <span className="hint">id {media.id_media}</span>
              </div>
              <div className="body">
                {draft === null ? (
                  <>
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
                  </>
                ) : (
                  <>
                    <div className="row">
                      <label htmlFor="ed-title">Title</label>
                      <input
                        id="ed-title"
                        type="text"
                        value={draft.title}
                        onChange={(event) => edit({ title: event.target.value })}
                        spellCheck={false}
                        autoComplete="off"
                        autoFocus
                        disabled={busy}
                      />
                    </div>
                    <div className="row">
                      <label htmlFor="ed-kind">Kind</label>
                      <select
                        id="ed-kind"
                        value={draft.kind}
                        onChange={(event) =>
                          edit({ kind: event.target.value as Draft['kind'] })
                        }
                        disabled={busy}
                      >
                        <option value="film">Film</option>
                        <option value="series">Series</option>
                        <option value="documentary">Documentary</option>
                      </select>
                    </div>
                    <div className="row">
                      <label htmlFor="ed-year">Year</label>
                      <input
                        id="ed-year"
                        type="text"
                        inputMode="numeric"
                        value={draft.year}
                        onChange={(event) => edit({ year: event.target.value })}
                        placeholder="2021"
                        autoComplete="off"
                        disabled={busy}
                      />
                    </div>
                    <div className="row">
                      <label htmlFor="ed-genres">Genres</label>
                      <input
                        id="ed-genres"
                        type="text"
                        value={draft.genres}
                        onChange={(event) => edit({ genres: event.target.value })}
                        placeholder="Sci-fi, Drama"
                        autoComplete="off"
                        disabled={busy}
                      />
                    </div>
                    <div className="row tall">
                      <label htmlFor="ed-synopsis">Synopsis</label>
                      <textarea
                        id="ed-synopsis"
                        rows={3}
                        value={draft.synopsis}
                        onChange={(event) => edit({ synopsis: event.target.value })}
                        disabled={busy}
                      />
                    </div>
                    <div className="row tall">
                      <label htmlFor="ed-notes">Notes</label>
                      <textarea
                        id="ed-notes"
                        rows={2}
                        value={draft.notes}
                        onChange={(event) => edit({ notes: event.target.value })}
                        disabled={busy}
                      />
                    </div>
                    <div className="row">
                      <label htmlFor="ed-inlist">My list</label>
                      <span className="check">
                        <input
                          id="ed-inlist"
                          type="checkbox"
                          checked={draft.in_list}
                          onChange={(event) =>
                            edit({ in_list: event.target.checked })
                          }
                          disabled={busy}
                        />
                      </span>
                    </div>
                    {/* Las unicas filas que se dibujan iterando: cuantas
                        pistas hay lo decide el archivo, no el formulario. */}
                    <div className="row tall">
                      <span className="label">Audio</span>
                      <div className="tracks">
                        {media.audio_tracks.map((track) => (
                          <label className="track" key={track.index}>
                            <input
                              type="checkbox"
                              checked={!draft.ignoredAudio.has(track.index)}
                              onChange={(event) =>
                                edit({
                                  ignoredAudio: toggled(
                                    draft.ignoredAudio,
                                    track.index,
                                    event.target.checked,
                                  ),
                                })
                              }
                              disabled={busy}
                            />
                            <span>{audioLabel(track)}</span>
                          </label>
                        ))}
                      </div>
                    </div>

                    <div className="row tall">
                      <span className="label">Subtitles</span>
                      <div className="tracks">
                        {media.subtitle_tracks.length === 0 && (
                          <span className="empty">Sin subtítulos</span>
                        )}
                        {media.subtitle_tracks.map((track) => (
                          <label className="track" key={track.index}>
                            <input
                              type="checkbox"
                              checked={!draft.ignoredSubtitles.has(track.index)}
                              onChange={(event) =>
                                edit({
                                  ignoredSubtitles: toggled(
                                    draft.ignoredSubtitles,
                                    track.index,
                                    event.target.checked,
                                  ),
                                })
                              }
                              disabled={busy}
                            />
                            <span>{subtitleLabel(track)}</span>
                          </label>
                        ))}
                      </div>
                    </div>

                    <p className="note">
                      Lo destildado no se genera al reproducir. Un campo que se
                      deja vacío borra el dato; lo que dicta ffprobe —codecs,
                      duración, pistas— no se edita desde acá.
                    </p>
                  </>
                )}
              </div>
            </div>
          )}

          <div className="buttons">
            {/* Un <a> y no un Link: cada vista es una pagina propia y no hay
                router del lado del cliente. */}
            <a className="dlg-btn" href="/">
              Back to library
            </a>
            {draft !== null ? (
              <>
                <button
                  className="dlg-btn"
                  type="button"
                  onClick={() => setDraft(null)}
                  disabled={busy}
                >
                  Cancelar
                </button>
                {/* type="button": el submit de este <form> es el alta. */}
                <button
                  className="dlg-btn default"
                  type="button"
                  onClick={() => void save()}
                  disabled={busy || draft.title.trim() === ''}
                >
                  Guardar
                </button>
              </>
            ) : (
              <>
                <button
                  className="dlg-btn"
                  type="button"
                  onClick={reset}
                  disabled={busy}
                >
                  {media === null ? 'Limpiar' : 'Cargar otro'}
                </button>
                {media !== null && (
                  <button
                    className="dlg-btn"
                    type="button"
                    onClick={() => setDraft(draftFrom(media))}
                    disabled={busy}
                  >
                    Editar
                  </button>
                )}
                <button
                  className="dlg-btn default"
                  type="submit"
                  disabled={busy || target === ''}
                >
                  Add to library
                </button>
              </>
            )}
          </div>
        </div>
      </form>

      <StatusBar
        state={state(busy, editing, error, media)}
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
 * Los campos editoriales como los maneja un formulario: todo string, porque es
 * lo que devuelve un <input>. La traduccion a los tipos de la API —el ano a
 * numero, los generos a lista, el campo vacio a null— la hace `toPatch`.
 */
interface Draft {
  title: string
  kind: NonNullable<MediaPatch['kind']>
  year: string
  genres: string
  synopsis: string
  notes: string
  in_list: boolean
  // Los indices destildados. Se guarda lo ignorado y no lo seleccionado porque
  // es lo que viaja en el PATCH, y asi no hay que invertir la lista al guardar.
  ignoredAudio: Set<number>
  ignoredSubtitles: Set<number>
}

/** Los indices de las pistas que ya vienen marcadas como ignoradas. */
function ignoredFrom(tracks: { index: number; ignore: boolean }[]): Set<number> {
  return new Set(tracks.filter((track) => track.ignore).map((track) => track.index))
}

function draftFrom(media: MediaResponse): Draft {
  return {
    title: media.title,
    // La columna es NOT NULL con default 'film' y `MediaPatch` solo acepta esos
    // tres valores, asi que lo que vuelve del servidor es uno de los tres.
    kind: media.kind as Draft['kind'],
    year: media.year === null ? '' : String(media.year),
    genres: media.genres.join(', '),
    synopsis: media.synopsis ?? '',
    notes: media.notes ?? '',
    in_list: media.in_list,
    ignoredAudio: ignoredFrom(media.audio_tracks),
    ignoredSubtitles: ignoredFrom(media.subtitle_tracks),
  }
}

/**
 * El borrador entero, no solo lo que cambio.
 *
 * Es un PATCH y podria mandar unicamente los campos tocados, pero el
 * formulario muestra *todos* los editables: no hay nada oculto que un campo
 * ausente pudiera estar protegiendo, y mandar el borrador completo evita
 * llevar la cuenta de lo que se toco. Lo que si importa es el null: un campo
 * que se deja vacio viaja como `null` y borra la columna, que es justo lo que
 * el PATCH distingue de no mandarlo.
 */
function toPatch(draft: Draft): MediaPatch {
  const year = Number.parseInt(draft.year, 10)
  const genres = draft.genres
    .split(',')
    .map((genre) => genre.trim())
    .filter((genre) => genre !== '')

  return {
    // Los tres NOT NULL: se cambian, no se vacian.
    title: draft.title.trim(),
    kind: draft.kind,
    in_list: draft.in_list,
    // Los nullables: vacio es un null explicito.
    year: Number.isNaN(year) ? null : year,
    genres: genres.length > 0 ? genres : null,
    synopsis: draft.synopsis.trim() || null,
    notes: draft.notes.trim() || null,
    // Las pistas: la lista reemplaza a la anterior, asi que `[]` es "generalas
    // todas" y no "no toques nada".
    ignored_audio: [...draft.ignoredAudio].sort((a, b) => a - b),
    ignored_subtitles: [...draft.ignoredSubtitles].sort((a, b) => a - b),
  }
}

/** Marca o desmarca una pista sin mutar el Set que esta en el estado. */
function toggled(ignored: Set<number>, index: number, keep: boolean): Set<number> {
  const next = new Set(ignored)
  if (keep) next.delete(index)
  else next.add(index)
  return next
}

/**
 * `invalid_path` significa lo contrario en los dos endpoints que lo usan: el
 * player manda la ruta absoluta y el alta la relativa. El mensaje compartido
 * habla del caso del player, asi que aca se traduce.
 *
 * El 422 no tiene slug: FastAPI contesta los errores de validacion con su
 * propio formato, asi que sin este caso saldria el mensaje de "no se pudo
 * conectar", que manda a buscar el problema al lugar equivocado.
 */
function failure(err: ApiError): string {
  if (err.slug === 'invalid_path') {
    return 'La ruta tiene que ser relativa a MEDIA_ROOT.'
  }
  if (err.status === 422) {
    return 'Hay un campo con un valor que el servidor no acepta.'
  }
  return errorMessage(err.slug)
}

function state(
  busy: boolean,
  editing: boolean,
  error: string | null,
  media: MediaResponse | null,
): string {
  if (busy) return editing ? 'Guardando la ficha…' : 'Analizando el archivo…'
  if (error !== null) return 'Error'
  if (editing) return 'Editando la ficha'
  if (media !== null) return `Added · ${media.title}`
  return 'Ready to add'
}
