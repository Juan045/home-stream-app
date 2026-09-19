/**
 * Estado de la codificacion de biblioteca de una ficha.
 *
 * Es el mismo patron que `useSession`, con dos diferencias que vienen del
 * trabajo que sigue: dura horas en vez de minutos, asi que el polling es mucho
 * mas espaciado; y no hay sesion ni heartbeat detras, asi que dejar la pagina
 * no lo detiene. El servidor sigue codificando igual.
 */
import { useCallback, useEffect, useState } from 'react'
import {
  ApiError,
  errorMessage,
  readEncode,
  startEncode,
  type EncodeResponse,
} from '../../../api/client'

/** Seis horas de trabajo no necesitan que se pregunte cada dos segundos. */
const POLL_MS = 5000

export interface EncodeState {
  encode: EncodeResponse | null
  error: string | null
  /** True mientras el POST esta en vuelo, para no mandarlo dos veces. */
  starting: boolean
  start: () => void
}

/** Lo ultimo que contesto el servidor, con el id que lo pidio. */
interface Result {
  id: string
  encode: EncodeResponse | null
  error: string | null
}

export function useEncode(idMedia: string | null): EncodeState {
  const [result, setResult] = useState<Result | null>(null)
  const [starting, setStarting] = useState(false)

  // El id viaja adentro del resultado en vez de limpiarse al cambiar de ficha:
  // asi no hay que llamar a setState en el cuerpo del efecto, y lo que quedo en
  // mano de la ficha anterior se descarta al derivar.
  useEffect(() => {
    if (idMedia === null) return

    const controller = new AbortController()
    void readEncode(idMedia, controller.signal)
      .then((encode) => setResult({ id: idMedia, encode, error: null }))
      .catch((err: unknown) => {
        if (controller.signal.aborted) return
        setResult({
          id: idMedia,
          encode: null,
          error: errorMessage(err instanceof ApiError ? err.slug : 'unknown'),
        })
      })

    return () => controller.abort()
  }, [idMedia])

  const fresh = idMedia !== null && result !== null && result.id === idMedia
  const encode = fresh ? result.encode : null
  const error = fresh ? result.error : null

  // Mientras corre, se repregunta. `setTimeout` reprogramado y no
  // `setInterval`: si una respuesta tarda mas que el intervalo, el interval
  // encima pedidos.
  useEffect(() => {
    if (idMedia === null || encode === null) return
    if (encode.state !== 'building' && encode.state !== 'pending') return

    let cancelled = false
    const timer = setTimeout(() => {
      void readEncode(idMedia)
        .then((next) => {
          if (!cancelled) setResult({ id: idMedia, encode: next, error: null })
        })
        .catch((err: unknown) => {
          if (cancelled) return
          setResult({
            id: idMedia,
            encode: null,
            error: errorMessage(err instanceof ApiError ? err.slug : 'unknown'),
          })
        })
    }, POLL_MS)

    return () => {
      cancelled = true
      clearTimeout(timer)
    }
  }, [idMedia, encode])

  const start = useCallback(() => {
    if (idMedia === null) return
    setStarting(true)
    void startEncode(idMedia)
      .then((next) => setResult({ id: idMedia, encode: next, error: null }))
      .catch((err: unknown) => {
        setResult({
          id: idMedia,
          encode: null,
          error: errorMessage(err instanceof ApiError ? err.slug : 'unknown'),
        })
      })
      .finally(() => setStarting(false))
  }, [idMedia])

  return { encode, error, starting, start }
}
