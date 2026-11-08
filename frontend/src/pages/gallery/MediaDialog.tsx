/**
 * El detalle de una ficha de la biblioteca (mockups/detail.html). El disco de la
 * costura dice si se puede ver ya, con `GET /media/{id}/encode`.
 *
 * <dialog> nativo con showModal(): capa superior, fondo inerte, Escape y foco los
 * da el navegador. Todo cierre pasa por dialog.close(), que devuelve el foco al
 * boton que lo abrio y despues avisa con onClose.
 */
import { useEffect, useRef, type MouseEvent } from 'react'
import type { MediaResponse } from '../../api/client'
import { Icon } from '../../components/Icon'
import { KINDS, formatRuntime } from '../../format'
import { Artwork } from './Artwork'
import { availability } from './availability'
import { useEncode } from './hooks/useEncode'
import { langs, playerUrl, quality } from './shared'

interface Props {
  media: MediaResponse
  /** True mientras el PATCH de Mi lista de esta ficha esta en vuelo. */
  saving: boolean
  onToggleList: (media: MediaResponse) => void
  onClose: () => void
}

export function MediaDialog({ media, saving, onToggleList, onClose }: Props) {
  const dialog = useRef<HTMLDialogElement>(null)
  const pressedBackdrop = useRef(false)
  const disc = availability(useEncode(media.id_media).encode)
  const audio = langs(media.audio_tracks)
  const subs = langs(media.subtitle_tracks)

  // Sin cleanup que cierre: StrictMode corre el efecto dos veces y un close()
  // dispararia onClose, que desmonta el dialogo. Por eso se mira `open` antes.
  useEffect(() => {
    const element = dialog.current
    if (element !== null && !element.open) element.showModal()
  }, [])

  const close = () => dialog.current?.close()

  // El contenido tapa todo el dialogo: un target igual al dialogo es el fondo.
  // El clic tambien tiene que empezar ahi, asi seleccionar la sinopsis y soltar afuera no cierra.
  const onMouseDown = (event: MouseEvent<HTMLDialogElement>) => {
    pressedBackdrop.current = event.target === event.currentTarget
  }
  const onClick = (event: MouseEvent<HTMLDialogElement>) => {
    if (pressedBackdrop.current && event.target === event.currentTarget) close()
  }

  return (
    <dialog ref={dialog} className="detail-dialog" aria-labelledby="detail-title" onClose={onClose} onMouseDown={onMouseDown} onClick={onClick}>
      <button className="icon-btn detail-close" type="button" aria-label="Cerrar" onClick={close}><Icon name="x" /></button>

      <section className="detail-stage">
        <div className="detail-art"><Artwork media={media} /></div>
        <header className="detail-head">
          <p className="detail-eyebrow">{KINDS[media.kind] ?? media.kind}{media.year !== null && ` · ${media.year}`}</p>
          <h2 id="detail-title">{media.title}</h2>
          {media.genres.length > 0 && (
            <div className="quality">{media.genres.map((g) => <span className="q" key={g}>{g}</span>)}</div>
          )}
        </header>
        {media.synopsis && (
          <div className="detail-synopsis">
            <span className="detail-label">Sinopsis</span>
            <p>{media.synopsis}</p>
          </div>
        )}
      </section>

      <aside className="detail-side">
        <div className="detail-disc" data-tone={disc.tone} role="img" aria-label={disc.label}>
          <b>{disc.big}</b>
          <small>{disc.small}</small>
        </div>
        <div>
          <span className="detail-label">Duración</span>
          <div className="detail-runtime">{formatRuntime(media.duration)}</div>
        </div>
        <dl className="detail-facts">
          <div><dt className="detail-label">Calidad</dt><dd>{quality(media).join(' · ')}</dd></div>
          <div><dt className="detail-label">Audio</dt><dd>{audio || '—'}</dd></div>
          <div><dt className="detail-label">Subtítulos</dt><dd>{subs || '—'}</dd></div>
        </dl>
        <div className="detail-actions">
          {/* Un <a> y no un boton: reproducir es navegar a otra pagina. */}
          <a className="btn-primary" href={playerUrl(media.id_media)}><Icon name="play" />Ver función</a>
          <button
            className="btn-ghost"
            type="button"
            aria-pressed={media.in_list}
            aria-label={media.in_list ? 'Quitar de Mi lista' : 'Añadir a Mi lista'}
            title={media.in_list ? 'En tu lista' : 'Añadir a Mi lista'}
            disabled={saving}
            onClick={() => onToggleList(media)}
          >
            <Icon name={media.in_list ? 'check' : 'plus'} />
          </button>
        </div>
      </aside>
    </dialog>
  )
}
