/**
 * Lo que ocupa el cache, medido a pedido.
 *
 * Sin efecto de carga: el servidor recorre el disco en cada pedido y con el cache
 * lleno sobre un bind mount puede tardar, asi que solo se mide al llamar a
 * `measure`. Mientras mide, `measuring` deshabilita el boton, y eso tambien evita
 * dos recorridos a la vez.
 */
import { useCallback, useEffect, useRef, useState } from 'react'
import { readCacheUsage, type CacheUsage } from '../../../api/client'
import { toFailure, type Failure } from '../media'

/** Una medicion y cuando se hizo: es una foto, no un valor vivo. */
export interface Measurement {
  usage: CacheUsage
  at: Date
}

export function useCacheUsage() {
  const [measurement, setMeasurement] = useState<Measurement | null>(null)
  const [failure, setFailure] = useState<Failure | null>(null)
  const [measuring, setMeasuring] = useState(false)
  const controller = useRef<AbortController | null>(null)

  // Al dejar la pagina se corta la espera; el recorrido del servidor termina igual.
  useEffect(() => () => controller.current?.abort(), [])

  const measure = useCallback(() => {
    const current = new AbortController()
    controller.current = current
    setMeasuring(true)
    setFailure(null)
    void readCacheUsage(current.signal)
      // Un fallo deja la medicion anterior a la vista, con el aviso encima.
      .then((usage) => setMeasurement({ usage, at: new Date() }))
      .catch((err: unknown) => {
        if (!current.signal.aborted) setFailure(toFailure(err))
      })
      .finally(() => setMeasuring(false))
  }, [])

  return { measurement, failure, measuring, measure }
}
