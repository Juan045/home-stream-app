/**
 * hls.js dentro de React.
 *
 * Tres reglas, y casi todos los bugs del player salen de romperlas:
 *  1. La instancia va en un ref, no en estado: no es dato de render.
 *  2. Se crea y se destruye en el mismo efecto. Sin destroy() en el cleanup,
 *     StrictMode deja dos instancias sobre el mismo <video> pidiendo segmentos.
 *  3. La unica dependencia es la URL del master (mas un nonce explicito de
 *     reintento). Si entra el volumen o la pista elegida, el player se
 *     reconstruye y se pierde currentTime.
 */
import { useCallback, useEffect, useRef, useState } from 'react'
import Hls from 'hls.js'

export interface HlsAudioTrack {
  id: number
  label: string
  /** Conteo de canales del atributo CHANNELS del master, si vino. */
  channels?: string
}

export interface HlsPlayer {
  audioTracks: HlsAudioTrack[]
  currentAudio: number
  selectAudio: (id: number) => void
  /** Alto del RESOLUTION del master: alimenta el "1080p" de la barra. */
  height: number | null
  /** La playlist paso de EVENT a VOD: el video quedo seekeable entero. */
  complete: boolean
  fatal: boolean
  /** Detalle del error de hls.js, para mostrarlo como codigo en el dialogo. */
  fatalDetail: string | null
  reload: () => void
}

/**
 * El ref del <video> lo crea el componente y entra por parametro: si el hook
 * lo devolviera junto a los datos de render, las reglas del React Compiler
 * tratarian todo el objeto devuelto como un ref.
 */
export function useHlsPlayer(
  src: string | null,
  videoRef: React.RefObject<HTMLVideoElement | null>,
): HlsPlayer {
  const hlsRef = useRef<Hls | null>(null)
  // Posicion a recuperar despues de un reintento.
  const resumeAtRef = useRef(0)

  const [audioTracks, setAudioTracks] = useState<HlsAudioTrack[]>([])
  const [currentAudio, setCurrentAudio] = useState(-1)
  const [height, setHeight] = useState<number | null>(null)
  const [complete, setComplete] = useState(false)
  const [fatal, setFatal] = useState(false)
  const [fatalDetail, setFatalDetail] = useState<string | null>(null)
  const [nonce, setNonce] = useState(0)

  const reload = useCallback(() => {
    const video = videoRef.current
    if (video) resumeAtRef.current = video.currentTime
    setFatal(false)
    setFatalDetail(null)
    setNonce((n) => n + 1)
  }, [videoRef])

  useEffect(() => {
    const video = videoRef.current
    if (!video || !src) return

    // Safari reproduce HLS nativo y expone las renditions en video.audioTracks.
    if (!Hls.isSupported()) {
      video.src = src
      return () => {
        video.removeAttribute('src')
        video.load()
      }
    }

    const hls = new Hls({
      enableWorker: true,
      maxBufferLength: 30,
      maxBufferHole: 0.5,
      backBufferLength: 120,
      // Mientras el build corre la playlist es EVENT y crece: el cliente pide
      // segmentos que todavia no existen. Reintentar generoso.
      fragLoadingMaxRetry: 6,
      manifestLoadingMaxRetry: 6,
      manifestLoadingRetryDelay: 1000,
    })
    hlsRef.current = hls

    hls.on(Hls.Events.MANIFEST_PARSED, (_e, data) => {
      // El master declara una sola variante: su RESOLUTION es la del video.
      setHeight(data.levels[0]?.height ?? null)
      if (resumeAtRef.current > 0) {
        video.currentTime = resumeAtRef.current
        resumeAtRef.current = 0
      }
    })

    hls.on(Hls.Events.AUDIO_TRACKS_UPDATED, (_e, data) => {
      setAudioTracks(
        data.audioTracks.map((t, i) => ({
          id: i,
          label: t.name || t.lang || `Pista ${i + 1}`,
          channels: t.channels,
        })),
      )
      setCurrentAudio(hls.audioTrack)
    })

    hls.on(Hls.Events.AUDIO_TRACK_SWITCHED, (_e, data) => setCurrentAudio(data.id))

    // EVENT -> VOD: cuando FFmpeg cierra la playlist deja de ser live.
    hls.on(Hls.Events.LEVEL_LOADED, (_e, data) => setComplete(!data.details.live))

    hls.on(Hls.Events.ERROR, (_e, data) => {
      if (!data.fatal) return
      switch (data.type) {
        case Hls.ErrorTypes.NETWORK_ERROR:
          hls.startLoad()
          break
        case Hls.ErrorTypes.MEDIA_ERROR:
          hls.recoverMediaError()
          break
        default:
          hls.destroy()
          setFatalDetail(data.details)
          setFatal(true)
          break
      }
    })

    hls.loadSource(src)
    hls.attachMedia(video)

    return () => {
      hls.destroy()
      hlsRef.current = null
    }
  }, [src, nonce, videoRef])

  // Cambiar de idioma es esto y nada mas. No se toca el servidor: el master
  // declara las pistas como renditions y el timeline no cambia.
  const selectAudio = useCallback((id: number) => {
    if (hlsRef.current) {
      hlsRef.current.audioTrack = id
      return
    }
    const video = videoRef.current
    if (!video) return
    // video.audioTracks no es estandar: solo Safari, que es justo donde cae
    // esta rama porque ahi no se usa hls.js.
    const native = (
      video as HTMLVideoElement & {
        audioTracks?: { length: number; [i: number]: { enabled: boolean } }
      }
    ).audioTracks
    if (!native) return
    for (let i = 0; i < native.length; i++) native[i].enabled = i === id
    setCurrentAudio(id)
  }, [videoRef])

  return {
    audioTracks,
    currentAudio,
    selectAudio,
    height,
    complete,
    fatal,
    fatalDetail,
    reload,
  }
}
