/**
 * Estado de la sesion: polling mientras el build corre.
 *
 * setTimeout reprogramado por cambio de session, no setInterval: si una
 * respuesta tarda mas que el intervalo, el interval encima pedidos.
 */
import { useCallback, useEffect, useState } from 'react'
import { ApiError, readSession, type StreamResponse } from '../../../api/client'

export interface SessionState {
  session: StreamResponse | null
  errorSlug: string | null
  setSession: (s: StreamResponse) => void
  setErrorSlug: (slug: string) => void
}

export function useSession(): SessionState {
  const [session, setSession] = useState<StreamResponse | null>(null)
  const [errorSlug, setErrorSlug] = useState<string | null>(null)

  useEffect(() => {
    if (!session || session.status !== 'processing') return

    let cancelled = false
    const timer = setTimeout(() => {
      void readSession(session.session_id)
        .then((next) => {
          if (!cancelled) setSession(next)
        })
        .catch((err: unknown) => {
          if (cancelled) return
          setErrorSlug(err instanceof ApiError ? err.slug : 'unknown')
        })
    }, 2000)

    return () => {
      cancelled = true
      clearTimeout(timer)
    }
  }, [session])

  const replace = useCallback((s: StreamResponse) => setSession(s), [])
  const fail = useCallback((slug: string) => setErrorSlug(slug), [])

  return { session, errorSlug, setSession: replace, setErrorSlug: fail }
}
