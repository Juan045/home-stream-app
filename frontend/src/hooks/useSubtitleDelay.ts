/**
 * Corrimiento de los subtitulos, en el cliente.
 *
 * Los tiempos del WebVTT son absolutos respecto del original, igual que los
 * PTS de los segmentos: el pipeline ya los deja sincronizados. El retardo es
 * una preferencia del espectador, asi que se aplica moviendo los cues ya
 * cargados y nunca vuelve al servidor.
 *
 * Se lleva cuenta de cuanto se movio cada pista (`applied`) porque los cues se
 * mutan en su lugar: lo que hay que aplicar es la diferencia, no el total.
 */
import { useEffect, useRef } from 'react'

export function useSubtitleDelay(
  videoRef: React.RefObject<HTMLVideoElement | null>,
  subUrl: string | null,
  delay: number,
): void {
  const applied = useRef(0)
  const lastUrl = useRef<string | null>(null)

  useEffect(() => {
    // Pista nueva: React reemplazo el <track>, sus cues vienen sin mover.
    if (lastUrl.current !== subUrl) {
      lastUrl.current = subUrl
      applied.current = 0
    }

    const video = videoRef.current
    if (!video || !subUrl) return

    const element = video.querySelector('track')
    if (!element) return

    const shift = (): boolean => {
      const cues = element.track.cues
      // Todavia sin parsear: con mode "disabled" el navegador ni la descarga.
      if (!cues || cues.length === 0) return false
      const delta = delay - applied.current
      applied.current = delay
      if (delta === 0) return true
      for (let i = 0; i < cues.length; i++) {
        const cue = cues[i]
        cue.startTime = Math.max(0, cue.startTime + delta)
        cue.endTime = Math.max(0, cue.endTime + delta)
      }
      return true
    }

    if (shift()) return

    const onLoad = () => {
      applied.current = 0
      shift()
    }
    element.addEventListener('load', onLoad)
    return () => element.removeEventListener('load', onLoad)
  }, [videoRef, subUrl, delay])
}
