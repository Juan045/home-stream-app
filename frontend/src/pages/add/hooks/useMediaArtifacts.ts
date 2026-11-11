/**
 * El estado de cada artefacto de una ficha, para el dialogo "Ver estado".
 *
 * Pide al abrir y repite cada 3 s solo mientras algo este en `building`: fuera de
 * eso nada cambia solo (un `pending` espera a que alguien abra el titulo). Al
 * cerrar el dialogo se desmonta y se aborta lo que este en vuelo. El endpoint
 * solo lee, asi que repreguntar no lanza FFmpeg ni altera el orden del GC.
 *
 * `reload` vuelve a pedir despues de algo que cambia el estado desde el dialogo,
 * como arrancar la codificacion: si el video quedo en `building`, el polling
 * arranca solo con esa respuesta.
 */
import { useEffect, useState, type Dispatch, type SetStateAction } from 'react'
import { readMediaArtifacts, type MediaArtifacts } from '../../../api/client'
import { toFailure, type Failure } from '../media'

const POLL_MS = 3000

interface Result {
  artifacts: MediaArtifacts | null
  failure: Failure | null
  /** La hora de la ultima respuesta buena. */
  at: Date | null
}

const isBuilding = (artifacts: MediaArtifacts) =>
  [artifacts.video, ...artifacts.audio, ...artifacts.subtitles].some((a) => a.state === 'building')

/**
 * Un fallo conserva lo ultimo que se vio: si fue un corte de red, el siguiente
 * pedido lo recupera sin que el tablero desaparezca.
 */
function load(idMedia: string, signal: AbortSignal, setResult: Dispatch<SetStateAction<Result | null>>) {
  return readMediaArtifacts(idMedia, signal)
    .then((artifacts) => setResult({ artifacts, failure: null, at: new Date() }))
    .catch((err: unknown) => {
      if (signal.aborted) return
      setResult((prev) => ({ artifacts: prev?.artifacts ?? null, failure: toFailure(err), at: prev?.at ?? null }))
    })
}

export function useMediaArtifacts(idMedia: string) {
  const [result, setResult] = useState<Result | null>(null)
  // Solo existe para que `reload` vuelva a correr el efecto, que aborta el pedido anterior.
  const [tick, setTick] = useState(0)

  // El dialogo se monta por ficha: no hay resultado de otra que descartar.
  useEffect(() => {
    const controller = new AbortController()
    void load(idMedia, controller.signal, setResult)
    return () => controller.abort()
  }, [idMedia, tick])

  // `setTimeout` reprogramado por cada respuesta y no `setInterval`: si una tarda
  // mas que el intervalo, el interval encimaria pedidos (mismo patron que useEncode).
  useEffect(() => {
    if (result?.artifacts == null || !isBuilding(result.artifacts)) return

    const controller = new AbortController()
    const timer = setTimeout(() => void load(idMedia, controller.signal, setResult), POLL_MS)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [idMedia, result])

  const artifacts = result?.artifacts ?? null
  return {
    artifacts,
    failure: result?.failure ?? null,
    checkedAt: result?.at ?? null,
    live: artifacts !== null && isBuilding(artifacts),
    reload: () => setTick((n) => n + 1),
  }
}
