import { PlayIcon } from '../../components/icons'
import { MISSING, mediaFacts } from '../../format'
import type { EncodeState } from './hooks/useEncode'
import type { Detail } from './hooks/useMediaDetail'

interface Props {
  detail: Detail
  encode: EncodeState
}

/** El panel de la derecha. Ocupa su ancho siempre, con o sin seleccion. */
export function DetailPanel({ detail, encode }: Props) {
  const { media, loading, error } = detail

  if (loading || error !== null || media === null) {
    const note = loading
      ? 'Cargando la ficha…'
      : (error ?? 'Elegi un titulo para ver su ficha.')
    return (
      <aside className="side" aria-label="Detalle del titulo" aria-busy={loading}>
        <p className="side-empty">{note}</p>
      </aside>
    )
  }

  return (
    <aside className="side" aria-label="Detalle del titulo">
      <div className="poster big">
        <span>Image not Available</span>
      </div>

      <h2 className="name">{media.title}</h2>

      <dl className="facts">
        {mediaFacts(media).map(([label, value]) => (
          <div className="fact" key={label}>
            <dt>{label}</dt>
            <dd data-missing={value === MISSING}>{value}</dd>
          </div>
        ))}
      </dl>

      <p className="synopsis" data-missing={media.synopsis === null}>
        {media.synopsis ?? MISSING}
      </p>

      <div className="actions">
        {/* Un <a> y no un boton: cada vista es una pagina propia, asi que
            reproducir es navegar. El id_media va tal cual — la ruta absoluta la
            resuelve el backend, que es el unico que conoce MEDIA_ROOT. */}
        <a className="btn primary" href={`/player/?media=${media.id_media}`}>
          <PlayIcon /> Reproducir
        </a>
        <button className="btn" type="button" title="no available yet">
          Ver ficha
        </button>
        <Encode state={encode} />
      </div>
    </aside>
  )
}

const SLOTS = 12

/**
 * La codificacion de biblioteca. Cuatro estados y una sola accion.
 *
 * No hay boton de reintentar ni de recodificar: con el asset ya en el cache el
 * `POST` contesta 409 en cualquier estado, incluido `failed`. Un boton que solo
 * puede dar error miente sobre lo que el servidor deja hacer.
 */
function Encode({ state }: { state: EncodeState }) {
  const { encode, error, starting, start } = state

  if (error !== null) return <p className="encode-note">{error}</p>
  if (encode === null) return null

  if (encode.state === 'ready') {
    return (
      <p className="encode-note">
        Codificado · {encode.video_codec ?? 'desconocido'}
      </p>
    )
  }

  if (encode.state === 'failed') {
    return (
      <p className="encode-note">
        La codificacion fallo. Hay que borrarla del cache para reintentar.
      </p>
    )
  }

  if (encode.state === 'building' || encode.state === 'pending') {
    const percent = Math.round(encode.progress * 100)
    // `pending` es "en cola": el build todavia no arranco, asi que no hay
    // avance que mostrar y la barra va indeterminada.
    const queued = encode.state === 'pending'
    const filled = queued ? 4 : Math.max(1, Math.round(encode.progress * SLOTS))

    return (
      <div className="encode-run">
        <p className="encode-note">
          {queued ? 'En cola…' : `Codificando… ${percent}%`}
        </p>
        <div
          className="blocks"
          data-indeterminate={queued}
          role="progressbar"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={queued ? undefined : percent}
          aria-label="Codificacion"
        >
          {Array.from({ length: filled }, (_, i) => (
            <i key={i} />
          ))}
        </div>
      </div>
    )
  }

  return (
    <button className="btn" type="button" onClick={start} disabled={starting}>
      {starting ? 'Iniciando…' : 'Codificar AV1'}
    </button>
  )
}
