/**
 * Sin heartbeat la sesion deja de proteger su asset del GC a los 2 minutos.
 * El modo ?src no tiene sesion: ahi sessionId viene null y no corre nada.
 */
import { useEffect } from 'react'

const INTERVAL_MS = 30_000

export function useHeartbeat(sessionId: string | null): void {
  useEffect(() => {
    if (!sessionId) return
    const timer = setInterval(() => {
      void fetch(`/api/v1/heartbeat/${sessionId}`, { method: 'POST' }).catch(() => {})
    }, INTERVAL_MS)
    return () => clearInterval(timer)
  }, [sessionId])
}
