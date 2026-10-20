/**
 * Edicion de una ficha (/edit/?media=<id>): el paso edicion de mockups/add.html.
 *
 * Carga la ficha con `GET /media/{id_media}` y guarda los campos editoriales
 * con `PATCH /media/{id_media}`. Al guardar muestra la entrada de papel con la
 * ficha que devolvio el servidor, y desde ahi se puede volver a editar.
 */
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { errorMessage, readMedia, updateMedia, type MediaPatch, type MediaResponse } from '../../api/client'
import { Icon } from '../../components/Icon'
import { KINDS, formatRuntime } from '../../format'
import { AdminShell, FieldError, ServerAlert, Ticket } from './admin'
import { channels, codec, lang, toFailure, videoLine, type Failure } from './media'

// Se lee una vez: abrir otra ficha es navegar, no hay router.
const PARAM_ID = new URLSearchParams(location.search).get('media')

// Sin `?media=` no hay que pedir nada: es lo mismo que una ficha que no existe.
const NOT_FOUND: Failure = { msg: errorMessage('media_not_found'), slug: 'media_not_found' }

type Step = 'loading' | 'missing' | 'edit' | 'ticket'

/** Miga, titulo y bajada de cada paso. La entrada solo aparece despues de guardar. */
const HEAD: Record<Step, [string, string, string]> = {
  loading: ['Editar ficha', 'Editar la ficha', ''],
  missing: ['Editar ficha', 'Editar la ficha', ''],
  edit: ['Editar ficha', 'Editar la ficha', 'Los datos editoriales y qué pistas se generan al reproducir. Lo que dicta ffprobe no se edita.'],
  ticket: ['Ficha', 'Ficha actualizada', 'Así queda en la cartelera.'],
}

const KIND_ICON: Record<string, string> = { film: 'film', series: 'tv', documentary: 'doc' }

const when = (iso: string) =>
  new Date(iso).toLocaleString('es-AR', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' })

// Hoy un "20x4" se mandaba como null sin avisar y borraba el ano.
const yearBad = (year: string) => year.trim() !== '' && !/^\d{4}$/.test(year.trim())

export default function EditMediaPage() {
  const [step, setStep] = useState<Step>(PARAM_ID === null ? 'missing' : 'loading')
  const [busy, setBusy] = useState(false)
  const [media, setMedia] = useState<MediaResponse | null>(null)
  const [error, setError] = useState<Failure | null>(PARAM_ID === null ? NOT_FOUND : null)
  // Null cuando no se esta editando: el borrador nace al abrir la edicion y muere con ella.
  const [draft, setDraft] = useState<Draft | null>(null)
  // Que campos muestran su validacion: se marcan al salir del campo o al intentar guardar.
  const [shown, setShown] = useState({ title: false, year: false })
  // Lo que se esta escribiendo en generos, antes de volverse chip.
  const [genre, setGenre] = useState('')
  const [saved, setSaved] = useState(false)
  const [toast, setToast] = useState(false)
  const toastTimer = useRef(0)
  const titleRef = useRef<HTMLInputElement>(null)
  const yearRef = useRef<HTMLInputElement>(null)
  const genreRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (PARAM_ID === null) return
    const controller = new AbortController()
    void readMedia(PARAM_ID, controller.signal)
      .then((found) => {
        setMedia(found)
        setDraft(draftFrom(found))
        setStep('edit')
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return
        setError(toFailure(err))
        setStep('missing')
      })
    return () => controller.abort()
  }, [])

  const dirty =
    media !== null && draft !== null && JSON.stringify(toPatch(draft)) !== JSON.stringify(toPatch(draftFrom(media)))

  async function save(event: FormEvent) {
    event.preventDefault()
    if (media === null || draft === null || busy) return

    setShown({ title: true, year: true })
    if (draft.title.trim() === '') {
      titleRef.current?.focus()
      return
    }
    if (yearBad(draft.year)) {
      yearRef.current?.focus()
      return
    }

    setBusy(true)
    setError(null)
    try {
      // Se repinta con la ficha que devolvio el servidor, no con el borrador.
      setMedia(await updateMedia(media.id_media, toPatch(draft)))
      closeEdit()
      setSaved(true)
      setStep('ticket')
      setToast(true)
      clearTimeout(toastTimer.current)
      toastTimer.current = window.setTimeout(() => setToast(false), 2400)
    } catch (err: unknown) {
      setError(toFailure(err))
      window.scrollTo({ top: 0, behavior: 'smooth' })
    } finally {
      setBusy(false)
    }
  }

  function edit(fields: Partial<Draft>) {
    setDraft((current) => (current === null ? null : { ...current, ...fields }))
  }

  function openEdit(from: MediaResponse) {
    setDraft(draftFrom(from))
    setError(null)
    setStep('edit')
    window.scrollTo({ top: 0 })
  }

  function closeEdit() {
    setDraft(null)
    setError(null)
    setShown({ title: false, year: false })
    setGenre('')
  }

  function cancel() {
    if (dirty && !confirm('Hay cambios sin guardar. ¿Descartarlos?')) return
    // No se guardo nada: no hay entrada que mostrar.
    if (!saved) {
      location.assign('/')
      return
    }
    closeEdit()
    setStep('ticket')
  }

  /** La coma o el Enter cierran el genero; los repetidos no se agregan. */
  function addGenres(text: string) {
    const fresh = text.split(',').map((g) => g.trim()).filter((g) => g !== '')
    if (fresh.length === 0) return
    setDraft((current) => current && { ...current, genres: [...new Set([...current.genres, ...fresh])] })
  }

  function removeGenre(name: string) {
    setDraft((current) => current && { ...current, genres: current.genres.filter((g) => g !== name) })
  }

  const [crumb, heading, lead] = HEAD[step]

  return (
    <>
      <AdminShell crumb={crumb} heading={heading} lead={lead}>
        {step === 'missing' && error !== null && (
          <div className="form-view">
            <ServerAlert failure={error} />
            <a className="btn btn-outlined sl" href="/"><Icon name="chev-left" />Volver a la cartelera</a>
          </div>
        )}

        {step === 'ticket' && media !== null && (
          <>
            <Ticket media={media} />
            <div className="ticket-actions">
              <button className="btn btn-filled" type="button" onClick={() => openEdit(media)}><Icon name="edit" />Editar ficha</button>
              <a className="btn btn-outlined sl" href={`/player/?media=${encodeURIComponent(media.id_media)}`}><Icon name="play" />Ver función</a>
              <a className="btn btn-text sl" href="/">Volver a la cartelera</a>
            </div>
          </>
        )}

        {step === 'edit' && media !== null && draft !== null && (
          <>
            {error !== null && <ServerAlert failure={error} />}
            <form className="edit-grid" noValidate onSubmit={(event) => void save(event)}>
              <div>
                <section className="card">
                  {busy && <div className="progress" aria-hidden="true" />}
                  <div className="card-head">
                    <span className="ico"><Icon name="info" /></span>
                    <div><h2>Ficha</h2><p>Lo que se ve en la cartelera.</p></div>
                    <span className="api">PATCH /media/{media.id_media.slice(0, 8)}…</span>
                  </div>
                  <div className="card-body">
                    <div className="field">
                      <label className="label" htmlFor="f-title">Título <span className="req">*</span></label>
                      <input
                        ref={titleRef}
                        className="input"
                        id="f-title"
                        value={draft.title}
                        onChange={(event) => {
                          const title = event.target.value
                          edit({ title })
                          // Si ya se marco, se destraba apenas se corrige.
                          if (title.trim() !== '') setShown((s) => ({ ...s, title: false }))
                        }}
                        onBlur={() => setShown((s) => ({ ...s, title: true }))}
                        autoComplete="off"
                        spellCheck={false}
                        // Solo al volver de la entrada con "Editar ficha": entrando por URL
                        // el foco abriria el teclado del celular sin que nadie lo pida.
                        autoFocus={saved}
                        disabled={busy}
                        aria-invalid={shown.title && draft.title.trim() === ''}
                        aria-describedby={shown.title && draft.title.trim() === '' ? 'f-title-err' : undefined}
                      />
                      {shown.title && draft.title.trim() === '' && <FieldError id="f-title-err" msg="El título no puede quedar vacío." />}
                    </div>

                    <div className="field" role="radiogroup" aria-labelledby="l-kind">
                      <span className="label" id="l-kind">Tipo</span>
                      <div className="choices">
                        {Object.entries(KINDS).map(([kind, label]) => (
                          <label className="choice sl" key={kind}>
                            <input
                              className="radio"
                              type="radio"
                              name="kind"
                              value={kind}
                              checked={draft.kind === kind}
                              onChange={() => edit({ kind: kind as Draft['kind'] })}
                              disabled={busy}
                            />
                            <Icon name={KIND_ICON[kind]} /><b>{label}</b>
                          </label>
                        ))}
                      </div>
                    </div>

                    <div className="two">
                      <div className="field">
                        <label className="label" htmlFor="f-year"><Icon name="cal" />Año</label>
                        <input
                          ref={yearRef}
                          className="input short"
                          id="f-year"
                          value={draft.year}
                          onChange={(event) => {
                            const year = event.target.value
                            edit({ year })
                            if (!yearBad(year)) setShown((s) => ({ ...s, year: false }))
                          }}
                          onBlur={() => setShown((s) => ({ ...s, year: true }))}
                          inputMode="numeric"
                          maxLength={4}
                          placeholder="2021"
                          autoComplete="off"
                          disabled={busy}
                          aria-invalid={shown.year && yearBad(draft.year)}
                          aria-describedby={shown.year && yearBad(draft.year) ? 'f-year-err' : undefined}
                        />
                        {shown.year && yearBad(draft.year) && <FieldError id="f-year-err" msg="Cuatro dígitos, o vacío para borrarlo." />}
                      </div>
                      <div className="field">
                        <span className="label"><Icon name="bookmark" />Mi lista</span>
                        <label className="row sl">
                          <span className="txt">
                            <b>{draft.in_list ? 'Aparece en Mi lista' : 'No está en Mi lista'}</b>
                            <span>Las entradas de papel de la galería.</span>
                          </span>
                          <input
                            className="switch"
                            type="checkbox"
                            role="switch"
                            checked={draft.in_list}
                            onChange={(event) => edit({ in_list: event.target.checked })}
                            disabled={busy}
                          />
                        </label>
                      </div>
                    </div>

                    <div className="field">
                      <label className="label" htmlFor="f-genres"><Icon name="tag" />Géneros<span className="opt">Opcional</span></label>
                      <div
                        className="input combo"
                        // mousedown y no click: asi el campo no pierde el foco.
                        onMouseDown={(event) => {
                          if ((event.target as Element).closest('button, input')) return
                          event.preventDefault()
                          genreRef.current?.focus()
                        }}
                      >
                        {draft.genres.map((name) => (
                          <span className="chip" key={name}>
                            {name}
                            <button type="button" className="sl" aria-label={`Quitar ${name}`} onClick={() => removeGenre(name)} disabled={busy}>
                              <Icon name="x" />
                            </button>
                          </span>
                        ))}
                        <input
                          ref={genreRef}
                          id="f-genres"
                          value={genre}
                          onChange={(event) => {
                            const parts = event.target.value.split(',')
                            setGenre(parts.pop() ?? '')
                            addGenres(parts.join(','))
                          }}
                          onKeyDown={(event) => {
                            if (event.key === 'Enter') {
                              event.preventDefault() // no manda el formulario
                              addGenres(genre)
                              setGenre('')
                            } else if (event.key === 'Backspace' && genre === '' && draft.genres.length > 0) {
                              removeGenre(draft.genres[draft.genres.length - 1])
                            }
                          }}
                          // Lo escrito sin Enter tambien cuenta: si no, Guardar lo perderia.
                          onBlur={() => {
                            addGenres(genre)
                            setGenre('')
                          }}
                          placeholder={draft.genres.length > 0 ? '' : 'Agregar género…'}
                          autoComplete="off"
                          disabled={busy}
                        />
                      </div>
                      <p className="desc">Escribí un género y Enter o coma para agregarlo.</p>
                    </div>

                    <div className="field">
                      <label className="label" htmlFor="f-syn"><Icon name="text" />Sinopsis<span className="opt">Opcional</span></label>
                      <textarea className="input" id="f-syn" rows={3} value={draft.synopsis} onChange={(event) => edit({ synopsis: event.target.value })} disabled={busy} />
                      <div className="desc-row">
                        <p className="desc">Se muestra en el escenario y en la ficha flotante.</p>
                        <span className="count">{draft.synopsis.length}</span>
                      </div>
                    </div>

                    <div className="field">
                      <label className="label" htmlFor="f-notes"><Icon name="lock" />Notas<span className="opt">Opcional</span></label>
                      <textarea className="input" id="f-notes" rows={2} value={draft.notes} onChange={(event) => edit({ notes: event.target.value })} disabled={busy} />
                      <p className="desc">Solo para vos: no se muestran en la cartelera.</p>
                    </div>
                  </div>
                </section>

                <section className="card">
                  <div className="card-head">
                    <span className="ico"><Icon name="layers" /></span>
                    <div><h2>Pistas</h2><p>Lo destildado no se genera ni se ofrece al reproducir.</p></div>
                  </div>
                  <div className="card-body">
                    <div className="cols">
                      <div>
                        <h3><Icon name="audio" />Audio <small>{kept(media.audio_tracks.length, draft.ignoredAudio)}</small></h3>
                        <div className="stack">
                          {media.audio_tracks.map((track) => (
                            <TrackRow
                              key={track.index}
                              name={track.title || lang(track.language)}
                              // Sin titulo el nombre ya es el idioma: no se repite abajo.
                              meta={[track.title ? lang(track.language) : null, codec(track.codec), channels(track.channels)]}
                              keep={!draft.ignoredAudio.has(track.index)}
                              onChange={(keep) => edit({ ignoredAudio: toggled(draft.ignoredAudio, track.index, keep) })}
                              disabled={busy}
                            />
                          ))}
                        </div>
                      </div>
                      <div>
                        <h3>
                          <Icon name="cc" />Subtítulos{' '}
                          <small>{media.subtitle_tracks.length > 0 && kept(media.subtitle_tracks.length, draft.ignoredSubtitles)}</small>
                        </h3>
                        <div className="stack">
                          {media.subtitle_tracks.length === 0 && <p className="empty">El archivo no trae subtítulos.</p>}
                          {media.subtitle_tracks.map((track) => (
                            <TrackRow
                              key={track.index}
                              name={track.title || lang(track.language)}
                              meta={[track.title ? lang(track.language) : null, codec(track.codec)]}
                              keep={!draft.ignoredSubtitles.has(track.index)}
                              onChange={(keep) => edit({ ignoredSubtitles: toggled(draft.ignoredSubtitles, track.index, keep) })}
                              disabled={busy}
                            />
                          ))}
                        </div>
                      </div>
                    </div>
                  </div>
                </section>

                <section className="card">
                  <div className="card-head">
                    <span className="ico"><Icon name="film" /></span>
                    <div><h2>Video</h2><p>Cómo se arma el stream: {videoLine(media)}.</p></div>
                  </div>
                  <div className="card-body">
                    {/* Un radio y no dos checkbox: la decision es una sola y el
                        backend no acepta ni "ninguna" ni "las dos". */}
                    <div className="choices two-up" role="radiogroup" aria-label="Estrategia de video">
                      <StrategyChoice
                        value="remux"
                        icon="box"
                        title="Sin recodificar"
                        // Misma regla que aplica el backend: solo H.264 se reempaqueta.
                        text={media.video_codec === 'h264'
                          ? 'Reempaqueta el H.264 tal cual. Arranca enseguida y no pierde calidad.'
                          : `Solo para video H.264: este archivo es ${codec(media.video_codec)}.`}
                        checked={draft.strategy === 'remux'}
                        disabled={busy || media.video_codec !== 'h264'}
                        onPick={() => edit({ strategy: 'remux' })}
                      />
                      <StrategyChoice
                        value="transcode"
                        icon="cycle"
                        title="Transcodificar"
                        text="Recodifica a H.264. Sirve para cualquier códec y usa más CPU."
                        checked={draft.strategy === 'transcode'}
                        disabled={busy}
                        onPick={() => edit({ strategy: 'transcode' })}
                      />
                    </div>
                  </div>
                </section>

                <div className="actionbar">
                  <span className={!busy && dirty ? 'grow dirty' : 'grow'}>
                    <span className="dot" />
                    <span>{busy ? 'Guardando…' : dirty ? 'Cambios sin guardar · un campo vacío borra el dato' : 'Sin cambios'}</span>
                  </span>
                  <button className="btn btn-text sl" type="button" onClick={cancel} disabled={busy}>Cancelar</button>
                  <button className="btn btn-filled" type="submit" disabled={busy}>
                    {busy ? <><span className="spin" />Guardando</> : <><Icon name="ticket-check" />Guardar cambios</>}
                  </button>
                </div>
              </div>

              <aside className="card side">
                <div className="card-head">
                  <span className="ico"><Icon name="file" /></span>
                  <div><h2>Del archivo</h2><p>Lo dicta ffprobe: no se edita.</p></div>
                </div>
                <dl className="facts">
                  <div><dt>Archivo</dt><dd className="mono">{media.file_name}</dd></div>
                  <div><dt>Duración</dt><dd>{formatRuntime(media.duration)}</dd></div>
                  <div><dt>Video</dt><dd>{videoLine(media)}</dd></div>
                  <div><dt>Audio</dt><dd>{media.audio_tracks.length} pista{media.audio_tracks.length === 1 ? '' : 's'}</dd></div>
                  <div><dt>Subtítulos</dt><dd>{media.subtitle_tracks.length || 'Ninguno'}</dd></div>
                  <div><dt>Alta</dt><dd>{when(media.created_at)}</dd></div>
                  <div><dt>ID</dt><dd className="mono">{media.id_media}</dd></div>
                </dl>
                <div className="card-foot">
                  <a className="btn btn-text sl" href={`/player/?media=${encodeURIComponent(media.id_media)}`}><Icon name="play" />Ver función</a>
                </div>
              </aside>
            </form>
          </>
        )}
      </AdminShell>

      <div className={toast ? 'toast show' : 'toast'} role="status">
        <Icon name="check2" /><span>{toast ? 'Ficha guardada' : ''}</span>
      </div>
    </>
  )
}

function TrackRow(props: {
  name: string
  meta: (string | null)[]
  keep: boolean
  onChange: (keep: boolean) => void
  disabled: boolean
}) {
  return (
    <label className="row sl">
      <input className="check" type="checkbox" checked={props.keep} onChange={(event) => props.onChange(event.target.checked)} disabled={props.disabled} />
      <span className="txt"><b>{props.name}</b><span>{props.meta.filter(Boolean).join(' · ')}</span></span>
    </label>
  )
}

function StrategyChoice(props: {
  value: string
  icon: string
  title: string
  text: string
  checked: boolean
  disabled: boolean
  onPick: () => void
}) {
  return (
    <label className="choice big sl">
      <span className="tile"><Icon name={props.icon} /></span>
      <span className="txt"><b>{props.title}</b><span>{props.text}</span></span>
      <input className="radio" type="radio" name="strategy" value={props.value} checked={props.checked} onChange={props.onPick} disabled={props.disabled} />
    </label>
  )
}

/** "2 de 3": cuantas pistas quedan tildadas. */
function kept(total: number, ignored: Set<number>): string {
  return `${total - ignored.size} de ${total}`
}

/**
 * Los campos editoriales como los maneja un formulario: texto, porque es lo
 * que devuelve un <input>. La traduccion a los tipos de la API —el ano a
 * numero, el campo vacio a null— la hace `toPatch`.
 */
interface Draft {
  title: string
  kind: NonNullable<MediaPatch['kind']>
  strategy: NonNullable<MediaPatch['strategy']>
  year: string
  // Los chips del campo, ya recortados y sin repetidos.
  genres: string[]
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
    // `strategy` viene tipado como `string` (el OpenAPI de .NET no declara la
    // union todavia), asi que se estrecha a mano a los dos valores del backend.
    strategy: media.strategy === 'remux' ? 'remux' : 'transcode',
    year: media.year === null ? '' : String(media.year),
    genres: [...media.genres],
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

  return {
    // Los NOT NULL: se cambian, no se vacian.
    title: draft.title.trim(),
    kind: draft.kind,
    strategy: draft.strategy,
    in_list: draft.in_list,
    // Los nullables: vacio es un null explicito.
    year: Number.isNaN(year) ? null : year,
    genres: draft.genres.length > 0 ? draft.genres : null,
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
