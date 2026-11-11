/**
 * "Ver estado": cada artefacto HLS de una ficha, y el boton para codificarla para
 * la biblioteca mientras el video no se haya despachado.
 *
 * <dialog> nativo con showModal(): el foco atrapado, Escape y devolver el foco al
 * boton que lo abrio los hace el navegador. Las pistas salen de la ficha de la fila,
 * tambien las ignoradas, que no son artefacto; el estado sale por indice de
 * GET /admin/media/{id}/artifacts.
 */
import { useEffect, useRef, useState, type MouseEvent } from 'react'
import { ApiError, startEncode, type ArtifactStatus, type MediaResponse } from '../../api/client'
import { Icon } from '../../components/Icon'
import { formatClock, formatRuntime } from '../../format'
import { ServerAlert } from './admin'
import { ArtifactRow } from './ArtifactRow'
import { LEGEND, NAMES, legendKey } from './artifacts'
import { useMediaArtifacts } from './hooks/useMediaArtifacts'
import { channels, codec, lang, toFailure, videoLine, type Failure } from './media'

const UNBUILT = 'se genera al tocar Play o al codificar la biblioteca'

interface Track {
  index: number
  language: string
  ignore: boolean
}

/** La ficha de la fila puede ser mas vieja que el estado (otra pestaña la edito): sin dato, pendiente. */
const unknown = (index: number): ArtifactStatus => ({ index, state: 'pending', progress: null, queued: false, error: null })

/** Las filas de un grupo, con su contador: "2 de 3 listos · 1 ignorada". */
function trackGroup<T extends Track>(tracks: T[], artifacts: ArtifactStatus[], meta: (track: T) => string) {
  const wanted = tracks.filter((t) => !t.ignore).length
  const ignored = tracks.length - wanted
  const ready = artifacts.filter((a) => a.state === 'ready').length
  const tally = `${ready} de ${wanted} listos${ignored > 0 ? ` · ${ignored} ignorada${ignored > 1 ? 's' : ''}` : ''}`

  const rows = tracks.map((t) => (
    <ArtifactRow
      key={t.index}
      idx={`#${t.index}`}
      name={lang(t.language)}
      meta={meta(t)}
      artifact={t.ignore ? null : artifacts.find((a) => a.index === t.index) ?? unknown(t.index)}
    />
  ))
  return { tally, rows }
}

export function StatusDialog({ media, onClose }: { media: MediaResponse; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const pressedBackdrop = useRef(false)
  const { artifacts, failure, checkedAt, live, reload } = useMediaArtifacts(media.id_media)
  const [encoding, setEncoding] = useState(false)
  const [encodeFailure, setEncodeFailure] = useState<Failure | null>(null)

  // Sin cleanup que cierre: StrictMode corre el efecto dos veces y un close() dispararia
  // onClose, que desmonta el dialogo. Por eso se mira `open` antes de abrir.
  useEffect(() => {
    const element = dialog.current
    if (element !== null && !element.open) element.showModal()
  }, [])

  // Siempre con close(): el navegador devuelve el foco y despues avisa con onClose.
  const close = () => dialog.current?.close()

  // El contenido tapa todo el dialogo, asi que un target igual al dialogo es el fondo.
  // Se exige que el clic tambien empiece ahi: seleccionar el stderr y soltar afuera no cierra.
  const onMouseDown = (event: MouseEvent<HTMLDialogElement>) => {
    pressedBackdrop.current = event.target === event.currentTarget
  }
  const onClick = (event: MouseEvent<HTMLDialogElement>) => {
    if (pressedBackdrop.current && event.target === event.currentTarget) close()
  }

  // El POST vuelve enseguida. El avance lo muestra la fila del video: la recarga la
  // encuentra en building y desde ahi el hook repregunta solo.
  async function encode() {
    setEncoding(true)
    setEncodeFailure(null)
    try {
      await startEncode(media.id_media)
      reload()
    } catch (err: unknown) {
      // 409: el video se despacho despues de la ultima consulta (un Play en otra
      // pestana). No es un error que mostrar: la recarga trae el estado real.
      if (err instanceof ApiError && err.slug === 'asset_already_encoded') reload()
      else setEncodeFailure(toFailure(err))
    } finally {
      setEncoding(false)
    }
  }

  // Solo con el video sin despachar, que es lo que acepta el servidor (un interrumpido es un
  // pending que se relanza de cero): en cualquier otro estado contesta 409, y un boton que
  // solo puede dar error miente.
  const encodable = artifacts !== null && ['idle', 'pending', 'interrupted'].includes(artifacts.video.state)

  const all = artifacts === null ? [] : [artifacts.video, ...artifacts.audio, ...artifacts.subtitles]
  const ready = all.filter((a) => a.state === 'ready').length
  const strategy = media.strategy === 'remux' ? 'Remux' : 'Transcodifica'

  let board = failure === null ? <p className="notice">Consultando el estado…</p> : null
  if (artifacts !== null) {
    const video = artifacts.video
    const source = codec(media.video_codec)
    // Sin video despachado no hay codec de salida que mostrar: solo el de origen.
    const videoName = artifacts.video_codec === null ? source : `${source} → ${codec(artifacts.video_codec)}`
    const unbuilt = video.state === 'idle' || video.state === 'pending' || video.state === 'interrupted'
    const videoMeta = artifacts.pinned ? 'Codificación de biblioteca' : unbuilt ? `${strategy} · ${UNBUILT}` : strategy

    const audio = trackGroup(media.audio_tracks, artifacts.audio, (t) =>
      [codec(t.codec), channels(t.channels)].filter(Boolean).join(' · '))
    const subtitles = trackGroup(media.subtitle_tracks, artifacts.subtitles, (t) =>
      [codec(t.codec), t.title || null].filter(Boolean).join(' · '))

    board = (
      <>
        <ul className="legend" aria-label="Referencia de estados">
          {LEGEND.map((key) => {
            const count = all.filter((a) => legendKey(a) === key).length
            return (
              <li key={key} className={count > 0 ? `is-${key}` : `is-${key} zero`}>
                <span className="dot" aria-hidden="true" />{NAMES[key]} <b>{count}</b>
              </li>
            )
          })}
        </ul>

        {video.state === 'idle' && (
          <p className="notice">
            Esta ficha nunca se procesó: todavía no se generó nada en el cache. Audios y subtítulos se
            generan al guardar la ficha; el video, al tocar Play o al codificar la biblioteca.
          </p>
        )}
        {all.some((a) => a.state === 'interrupted') && (
          <p className="notice">
            Un build quedó a medias por un corte o un reinicio del servidor. Se relanza de cero al volver a abrir el título o al codificarlo.
          </p>
        )}

        <section className="st-group" aria-label="Video">
          <h3><Icon name="film" />Video</h3>
          <ul className="arts"><ArtifactRow idx="V" name={videoName} meta={videoMeta} artifact={video} /></ul>
        </section>
        <section className="st-group" aria-label="Audio">
          <h3><Icon name="audio" />Audio<small>{audio.tally}</small></h3>
          <ul className="arts">{audio.rows}</ul>
        </section>
        <section className="st-group" aria-label="Subtítulos">
          <h3><Icon name="cc" />Subtítulos<small>{subtitles.tally}</small></h3>
          <ul className="arts">{subtitles.rows}</ul>
        </section>
      </>
    )
  }

  let liveText = 'Consultando…'
  if (live) liveText = 'En vivo · actualiza cada 3 s'
  else if (checkedAt !== null) liveText = `Consultado a las ${formatClock(checkedAt)}`

  return (
    <dialog
      ref={dialog}
      className="status-dialog"
      aria-labelledby="status-title"
      onClose={onClose}
      onMouseDown={onMouseDown}
      onClick={onClick}
    >
      <article className="status">
        <button className="close" type="button" aria-label="Cerrar" onClick={close}><Icon name="x" /></button>

        <section className="stage" data-initial={media.title.slice(0, 1)}>
          <h2 className="title" id="status-title">{media.title}</h2>
          <div className="board-head">
            <span className="st-label">Artefactos HLS</span>
            <span className={live ? 'live on' : 'live'} role="status"><i />{liveText}</span>
          </div>
          {failure !== null && <ServerAlert failure={failure} />}
          {encodeFailure !== null && <ServerAlert failure={encodeFailure} />}
          {board}
        </section>

        <aside className="st-side">
          <div
            className={artifacts !== null && ready === all.length ? 'score done' : 'score'}
            role="img"
            aria-label={artifacts === null ? 'Sin datos todavía' : `${ready} de ${all.length} artefactos listos`}
          >
            <b>{artifacts === null ? '—' : `${ready}/${all.length}`}</b><small>Listos</small>
          </div>

          <dl className="st-facts">
            <div><dt className="st-label">Video de origen</dt><dd>{videoLine(media)}</dd></div>
            <div><dt className="st-label">Duración</dt><dd>{formatRuntime(media.duration)}</dd></div>
            <div><dt className="st-label">Estrategia</dt><dd>{media.strategy === 'remux' ? 'Remux (sin recodificar)' : 'Transcodifica'}</dd></div>
            {/* La carpeta dentro de Cache:Dir: regenerar hoy es borrarla a mano (TODO #21). */}
            <div>
              <dt className="st-label">Asset</dt>
              {media.asset_id ? <dd><code>{media.asset_id}</code></dd> : <dd className="soft">Sin asignar</dd>}
            </div>
            <div><dt className="st-label">Archivo</dt><dd><code>{media.file_path}</code></dd></div>
          </dl>

          <div className="st-actions">
            {encodable && (
              <button className="btn btn-filled" type="button" onClick={() => void encode()} disabled={encoding}>
                <Icon name="chip" />{encoding ? 'Iniciando…' : 'Codificar para biblioteca'}
              </button>
            )}
            <a className="btn btn-outlined" href={`/edit/?media=${encodeURIComponent(media.id_media)}`}><Icon name="edit" />Editar ficha</a>
          </div>
        </aside>
      </article>
    </dialog>
  )
}
