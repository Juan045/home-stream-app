/**
 * Las fichas de la Biblioteca del admin, con sus datos tecnicos.
 *
 * Es `GET /admin/media`: el mismo listado que la galeria pero con la ficha
 * completa en cada fila, asi la tabla no hace un `GET /media/{id}` por titulo.
 */
import { useEffect, useState } from 'react'
import { listAdminMedia, type AdminMediaListResponse } from '../../../api/client'
import { toFailure, type Failure } from '../media'

// Fichas por pagina. La API acepta hasta 100; la pagina se recorre con `offset`.
export const PAGE = 10

/** Lo ultimo que contesto el servidor, con la busqueda que lo pidio. */
interface Result {
  key: string
  page: AdminMediaListResponse | null
  failure: Failure | null
}

export function useAdminLibrary(q: string, sort: 'title' | 'added', offset: number) {
  const [result, setResult] = useState<Result | null>(null)
  const key = `${sort}|${q}|${offset}`

  useEffect(() => {
    const controller = new AbortController()
    void listAdminMedia({ q, sort, limit: PAGE, offset }, controller.signal)
      .then((page) => setResult({ key, page, failure: null }))
      // Un fallo conserva la pagina anterior: la tabla no desaparece por un corte.
      .catch((err: unknown) => {
        // El abort es la limpieza del efecto, no un fallo que mostrar.
        if (!controller.signal.aborted) setResult((prev) => ({ key, page: prev?.page ?? null, failure: toFailure(err) }))
      })
    return () => controller.abort()
  }, [key, q, sort, offset])

  // A diferencia de useLibrary, la pagina anterior queda a la vista mientras llega
  // la nueva: al paginar, vaciar la tabla haria saltar el pie con los botones.
  const fresh = result !== null && result.key === key
  return {
    page: result?.page ?? null,
    failure: fresh ? result.failure : null,
    loading: !fresh,
  }
}
