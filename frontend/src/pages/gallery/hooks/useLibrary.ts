/**
 * Las secciones del catalogo.
 *
 * Cada una es una llamada a `GET /media` con sus filtros, no un corte en el
 * cliente: por eso un titulo marcado aparece dos veces, en "My list" y en la
 * de su tipo. El `total` de cada respuesta es el contador del encabezado y no
 * cambia con la paginacion.
 *
 * Falta "Continue watching": necesita el progreso de reproduccion, y
 * `PUT /media/{id_media}/progress` todavia responde 501.
 */
import { useEffect, useState } from 'react'
import {
  ApiError,
  errorMessage,
  listMedia,
  type MediaListResponse,
  type MediaQuery,
} from '../../../api/client'

/** El maximo que acepta la API. Las flechas del riel todavia no paginan, asi
 *  que pedir la pagina entera es lo que evita esconder titulos en silencio. */
const PAGE = 100

const DEFINITIONS: { title: string; query: MediaQuery }[] = [
  { title: 'My list', query: { in_list: true } },
  { title: 'Series', query: { kind: 'series' } },
  { title: 'Films', query: { kind: 'film' } },
  { title: 'Documentaries', query: { kind: 'documentary' } },
]

export interface Section {
  title: string
  page: MediaListResponse
}

export interface Library {
  sections: Section[]
  /** Titulos que matchean la busqueda, sin los filtros de seccion. */
  total: number
  loading: boolean
  error: string | null
}

/** Lo ultimo que contesto el servidor, con la busqueda que lo pidio. */
interface Result {
  key: string
  sections: Section[]
  total: number
  error: string | null
}

export function useLibrary(q: string, sort: 'title' | 'added'): Library {
  const [result, setResult] = useState<Result | null>(null)
  const key = `${sort}|${q}`

  useEffect(() => {
    const controller = new AbortController()
    const { signal } = controller

    const pages = DEFINITIONS.map((section) =>
      listMedia({ ...section.query, q, sort, limit: PAGE }, signal),
    )
    // El contador del pie es el total sin los filtros de seccion. Se pide con
    // limit=1 porque interesa el numero, no las filas.
    const all = listMedia({ q, limit: 1 }, signal)

    void Promise.all([...pages, all])
      .then((responses) => {
        setResult({
          key,
          sections: DEFINITIONS.map((section, i) => ({
            title: section.title,
            page: responses[i],
          })),
          total: responses[responses.length - 1].total,
          error: null,
        })
      })
      .catch((err: unknown) => {
        // El abort es la limpieza del efecto, no un fallo que mostrar.
        if (signal.aborted) return
        setResult({
          key,
          sections: [],
          total: 0,
          error: errorMessage(err instanceof ApiError ? err.slug : 'unknown'),
        })
      })

    return () => controller.abort()
  }, [key, q, sort])

  // `loading` se deduce de que lo que hay en mano sea de otra busqueda. Guardar
  // el estado de carga aparte obligaria a escribirlo desde adentro del efecto,
  // que es un render en cascada y ademas se desincroniza facil.
  const fresh = result !== null && result.key === key

  return {
    sections: fresh ? result.sections : [],
    total: fresh ? result.total : 0,
    loading: !fresh,
    error: fresh ? result.error : null,
  }
}
