/**
 * El escenario: las ultimas agregadas, con la ficha entera de cada una.
 *
 * El listado trae solo lo que dibuja una tarjeta; la sinopsis, los generos y
 * los idiomas del escenario salen de `GET /media/{id_media}`.
 */
import { useEffect, useState } from 'react'
import { listMedia, readMedia, type MediaResponse } from '../../../api/client'

const STAGE = 4

/** `null` mientras carga. */
export function useStage(): MediaResponse[] | null {
  const [items, setItems] = useState<MediaResponse[] | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    const { signal } = controller

    void listMedia({ sort: 'added', limit: STAGE }, signal)
      .then((page) => Promise.all(page.items.map((m) => readMedia(m.id_media, signal))))
      .then(setItems)
      // Sin escenario la galeria sigue: el error del catalogo ya lo muestra la pagina.
      .catch(() => {
        if (!signal.aborted) setItems([])
      })

    return () => controller.abort()
  }, [])

  return items
}
