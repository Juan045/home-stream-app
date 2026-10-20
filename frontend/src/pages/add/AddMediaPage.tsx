/**
 * Alta de fichas (/new/): el paso alta de mockups/add.html, pagina de admin.
 *
 * Un campo y nada mas, porque `POST /media` recibe un solo dato: la ruta
 * relativa a MEDIA_ROOT. El titulo sale del nombre del archivo y los codecs,
 * la duracion y las pistas las dicta ffprobe en el servidor; todo eso aparece
 * en la entrada de papel cuando la ficha queda creada.
 *
 * "Editar ficha" lleva a /edit/?media=<id>, que tiene su propia pagina.
 */
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { createMedia, type MediaResponse } from '../../api/client'
import { Icon } from '../../components/Icon'
import { AdminShell, FieldError, Ticket } from './admin'
import { toFailure, type Failure } from './media'

export default function AddMediaPage() {
  const [path, setPath] = useState('')
  const [busy, setBusy] = useState(false)
  // Null mientras se esta en el alta; con la ficha creada se muestra la entrada.
  const [media, setMedia] = useState<MediaResponse | null>(null)
  const [error, setError] = useState<Failure | null>(null)
  const pathRef = useRef<HTMLInputElement>(null)

  // El campo de la ruta tiene el foco al entrar y despues de un error: se
  // deshabilita mientras ffprobe analiza y ahi lo pierde.
  useEffect(() => {
    if (media === null && !busy) pathRef.current?.focus()
  }, [media, busy])

  async function create(event: FormEvent) {
    event.preventDefault()
    const target = path.trim()
    if (target === '' || busy) return

    setBusy(true)
    setError(null)
    try {
      setMedia(await createMedia(target))
    } catch (err: unknown) {
      setError(toFailure(err))
    } finally {
      setBusy(false)
    }
  }

  function another() {
    setPath('')
    setMedia(null)
    setError(null)
  }

  if (media !== null) {
    return (
      <AdminShell crumb="Ficha creada" heading="Título agregado" lead="Ya está en la cartelera. Completá la ficha ahora o más tarde." adding>
        <Ticket media={media} />
        <div className="ticket-actions">
          <a className="btn btn-filled" href={`/edit/?media=${encodeURIComponent(media.id_media)}`}><Icon name="edit" />Editar ficha</a>
          <a className="btn btn-outlined sl" href={`/player/?media=${encodeURIComponent(media.id_media)}`}><Icon name="play" />Ver función</a>
          <button className="btn btn-text sl" type="button" onClick={another}><Icon name="plus" />Agregar otro</button>
        </div>
      </AdminShell>
    )
  }

  return (
    <AdminShell
      crumb="Agregar"
      heading="Agregar un título"
      lead="Indicá el archivo y el servidor lo analiza: título, duración, códecs y pistas salen del propio video."
      adding
    >
      <form className="card form-view" noValidate onSubmit={(event) => void create(event)}>
        {busy && <div className="progress" aria-hidden="true" />}
        <div className="card-head">
          <span className="ico"><Icon name="folder" /></span>
          <div><h2>Archivo</h2><p>El servidor lo analiza con ffprobe y crea la ficha.</p></div>
          <span className="api">POST /media</span>
        </div>
        <div className="card-body">
          <div className="field">
            <label className="label" htmlFor="path"><Icon name="file" />Ruta del archivo <span className="req">*</span></label>
            <div className="input group">
              <span className="addon" aria-hidden="true">MEDIA_ROOT /</span>
              <input
                ref={pathRef}
                id="path"
                type="text"
                value={path}
                onChange={(event) => setPath(event.target.value)}
                placeholder="films/Dune (2021).mkv"
                spellCheck={false}
                autoComplete="off"
                aria-invalid={error !== null}
                aria-describedby={error !== null ? 'pathDesc pathErr' : 'pathDesc'}
                disabled={busy}
              />
            </div>
            <p className="desc" id="pathDesc">
              Relativa al directorio de medios: <code>films/Dune (2021).mkv</code>, no <code>/media/films/…</code>. Solo <code>.mp4</code> y <code>.mkv</code>.
            </p>
            {error !== null && <FieldError id="pathErr" msg={error.msg} slug={error.slug} />}
          </div>
        </div>
        <div className="card-foot">
          <span className="grow">{busy ? 'Analizando el archivo…' : 'Título, duración, códecs y pistas salen del video.'}</span>
          <button className="btn btn-filled" type="submit" disabled={busy || path.trim() === ''}>
            {busy ? <><span className="spin" />Analizando</> : <><Icon name="ticket" />Agregar a la cartelera</>}
          </button>
        </div>
      </form>
    </AdminShell>
  )
}
