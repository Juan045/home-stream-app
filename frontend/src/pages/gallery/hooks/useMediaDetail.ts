/**
 * La ficha del titulo seleccionado: `GET /media/{id_media}`.
 *
 * Se pide aparte del listado porque el listado no la trae: cada fila solo
 * tiene lo que dibuja una tarjeta.
 */
import { useEffect, useState } from 'react'
import {
  ApiError,
  errorMessage,
  readMedia,
  type MediaResponse,
} from '../../../api/client'

export interface Detail {
  media: MediaResponse | null
  loading: boolean
  error: string | null
}

/** Lo ultimo que contesto el servidor, con el id que lo pidio. */
interface Result {
  id: string
  media: MediaResponse | null
  error: string | null
}

export function useMediaDetail(idMedia: string | null): Detail {
  const [result, setResult] = useState<Result | null>(null)

  useEffect(() => {
    if (idMedia === null) return

    const controller = new AbortController()
    const { signal } = controller

    // El abort es lo que hace que dos clicks seguidos terminen mostrando la
    // segunda ficha: sin el, una respuesta lenta de la primera llega despues y
    // pisa a la que el usuario esta mirando.
    void readMedia(idMedia, signal)
      .then((media) => setResult({ id: idMedia, media, error: null }))
      .catch((err: unknown) => {
        if (signal.aborted) return
        setResult({
          id: idMedia,
          media: null,
          error: errorMessage(err instanceof ApiError ? err.slug : 'unknown'),
        })
      })

    return () => controller.abort()
  }, [idMedia])

  // Mientras lo que hay en mano sea de otra ficha, esto esta cargando.
  const fresh = idMedia !== null && result !== null && result.id === idMedia

  return {
    media: fresh ? result.media : null,
    loading: idMedia !== null && !fresh,
    error: fresh ? result.error : null,
  }
}
