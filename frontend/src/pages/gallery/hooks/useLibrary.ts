/**
 * Las secciones del catalogo.
 *
 * Cada una es una llamada a `GET /media` con sus filtros, no un corte en el
 * cliente: por eso un titulo marcado aparece dos veces, en "Mi lista" y en la
 * de su tipo. El `total` de cada respuesta es el contador del menu y no
 * cambia con la paginacion.
 *
 * Falta "Retomar funcion": necesita el progreso de reproduccion, y
 * `PUT /media/{id_media}/progress` todavia responde 501.
 */
import { useCallback, useEffect, useState } from 'react'
import {
  ApiError,
  errorMessage,
  listMedia,
  type MediaListResponse,
  type MediaQuery,
} from '../../../api/client'

/** El maximo que acepta la API. No hay paginacion en la galeria, asi que
 *  pedir la pagina entera es lo que evita esconder titulos en silencio. */
const PAGE = 100

export type View = 'list' | 'film' | 'series' | 'documentary'

const DEFINITIONS: { view: View; query: MediaQuery }[] = [
  { view: 'list', query: { in_list: true } },
  { view: 'film', query: { kind: 'film' } },
  { view: 'series', query: { kind: 'series' } },
  { view: 'documentary', query: { kind: 'documentary' } },
]

export interface Library {
  sections: Record<View, MediaListResponse> | null
  /** Titulos que matchean la busqueda, sin los filtros de seccion. */
  total: number
  /** Los resultados de la busqueda sin filtro de seccion. Vacio sin busqueda. */
  hits: MediaListResponse | null
  loading: boolean
  error: string | null
  /** Vuelve a pedir todo sin pasar por "cargando": lo que hay queda a la vista. */
  reload: () => void
}

/** Lo ultimo que contesto el servidor, con la busqueda que lo pidio. */
interface Result {
  key: string
  sections: Record<View, MediaListResponse> | null
  hits: MediaListResponse | null
  error: string | null
}

export function useLibrary(q: string, sort: 'title' | 'added'): Library {
  const [result, setResult] = useState<Result | null>(null)
  const [version, setVersion] = useState(0)
  const key = `${sort}|${q}`

  useEffect(() => {
    const controller = new AbortController()
    const { signal } = controller

    const pages = DEFINITIONS.map((section) =>
      listMedia({ ...section.query, q, sort, limit: PAGE }, signal),
    )
    // El total sin los filtros de seccion. Sin busqueda interesa solo el
    // numero (limit=1); con busqueda tambien las filas, para la grilla.
    const all = listMedia({ q, sort, limit: q ? PAGE : 1 }, signal)

    void Promise.all([...pages, all])
      .then((responses) => {
        setResult({
          key,
          sections: Object.fromEntries(
            DEFINITIONS.map((section, i) => [section.view, responses[i]]),
          ) as Record<View, MediaListResponse>,
          hits: responses[responses.length - 1],
          error: null,
        })
      })
      .catch((err: unknown) => {
        // El abort es la limpieza del efecto, no un fallo que mostrar.
        if (signal.aborted) return
        setResult({
          key,
          sections: null,
          hits: null,
          error: errorMessage(err instanceof ApiError ? err.slug : 'unknown'),
        })
      })

    return () => controller.abort()
  }, [key, q, sort, version])

  const reload = useCallback(() => setVersion((v) => v + 1), [])

  // `loading` se deduce de que lo que hay en mano sea de otra busqueda. Guardar
  // el estado de carga aparte obligaria a escribirlo desde adentro del efecto,
  // que es un render en cascada y ademas se desincroniza facil. El `reload` no
  // cambia la clave: la respuesta nueva pisa a la vieja sin parpadeo.
  const fresh = result !== null && result.key === key

  return {
    sections: fresh ? result.sections : null,
    total: fresh && result.hits ? result.hits.total : 0,
    hits: fresh && q ? result.hits : null,
    loading: !fresh,
    error: fresh ? result.error : null,
    reload,
  }
}
