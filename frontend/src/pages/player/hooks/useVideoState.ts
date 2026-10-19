/**
 * Estado de reproduccion leido del <video>, que queda NO controlado.
 *
 * currentTime se toma del evento timeupdate (~4 por segundo), no de un
 * requestAnimationFrame: alcanza para el timecode y la barra, y no provoca un
 * render por frame. El unico momento en que se escribe video.currentTime es
 * cuando el usuario busca.
 */
import { useCallback, useEffect, useState } from 'react'

export interface VideoState {
  playing: boolean
  ended: boolean
  buffering: boolean
  currentTime: number
  duration: number
  /** Fin de lo que el navegador ya tiene cargado, para la franja de la linea de tiempo. */
  bufferedEnd: number
  rate: number
  volume: number
  muted: boolean
  fullscreen: boolean
  toggle: () => void
  seek: (seconds: number) => void
  setVolume: (v: number) => void
  toggleMute: () => void
  toggleFullscreen: () => void
}

export function useVideoState(
  videoRef: React.RefObject<HTMLVideoElement | null>,
  containerRef: React.RefObject<HTMLDivElement | null>,
  fallbackDuration: number,
): VideoState {
  const [playing, setPlaying] = useState(false)
  const [ended, setEnded] = useState(false)
  const [buffering, setBuffering] = useState(false)
  const [currentTime, setCurrentTime] = useState(0)
  const [duration, setDuration] = useState(0)
  const [bufferedEnd, setBufferedEnd] = useState(0)
  const [rate, setRate] = useState(1)
  const [volume, setVolumeState] = useState(1)
  const [muted, setMuted] = useState(false)
  const [fullscreen, setFullscreen] = useState(false)

  useEffect(() => {
    const video = videoRef.current
    if (!video) return

    const onPlay = () => {
      setPlaying(true)
      setEnded(false)
    }
    const onPause = () => setPlaying(false)
    const onEnded = () => setEnded(true)
    const onSeeking = () => setEnded(false)
    const onWaiting = () => setBuffering(true)
    // canplay y seeked tambien: en pausa no llega "playing" y el spinner
    // quedaria prendido despues de buscar.
    const onPlaying = () => setBuffering(false)
    const onRate = () => setRate(video.playbackRate)
    const onProgress = () => {
      const b = video.buffered
      for (let i = 0; i < b.length; i++) {
        if (b.start(i) <= video.currentTime + 0.5) setBufferedEnd(b.end(i))
      }
    }
    const onTime = () => setCurrentTime(video.currentTime)
    const onDuration = () => {
      // En modo EVENT la duracion del <video> crece con la playlist; la real
      // la sabe la API. Se toma la mayor.
      if (Number.isFinite(video.duration)) setDuration(video.duration)
    }
    const onVolume = () => {
      setVolumeState(video.volume)
      setMuted(video.muted)
    }

    video.addEventListener('play', onPlay)
    video.addEventListener('pause', onPause)
    video.addEventListener('ended', onEnded)
    video.addEventListener('seeking', onSeeking)
    video.addEventListener('waiting', onWaiting)
    video.addEventListener('playing', onPlaying)
    video.addEventListener('canplay', onPlaying)
    video.addEventListener('seeked', onPlaying)
    video.addEventListener('ratechange', onRate)
    video.addEventListener('progress', onProgress)
    video.addEventListener('timeupdate', onTime)
    video.addEventListener('durationchange', onDuration)
    video.addEventListener('volumechange', onVolume)

    return () => {
      video.removeEventListener('play', onPlay)
      video.removeEventListener('pause', onPause)
      video.removeEventListener('ended', onEnded)
      video.removeEventListener('seeking', onSeeking)
      video.removeEventListener('waiting', onWaiting)
      video.removeEventListener('playing', onPlaying)
      video.removeEventListener('canplay', onPlaying)
      video.removeEventListener('seeked', onPlaying)
      video.removeEventListener('ratechange', onRate)
      video.removeEventListener('progress', onProgress)
      video.removeEventListener('timeupdate', onTime)
      video.removeEventListener('durationchange', onDuration)
      video.removeEventListener('volumechange', onVolume)
    }
  }, [videoRef])

  useEffect(() => {
    const onChange = () => setFullscreen(document.fullscreenElement !== null)
    document.addEventListener('fullscreenchange', onChange)
    return () => document.removeEventListener('fullscreenchange', onChange)
  }, [])

  const toggle = useCallback(() => {
    const video = videoRef.current
    if (!video) return
    if (video.paused) void video.play().catch(() => {})
    else video.pause()
  }, [videoRef])

  const seek = useCallback(
    (seconds: number) => {
      const video = videoRef.current
      if (!video) return
      video.currentTime = seconds
      setCurrentTime(seconds)
    },
    [videoRef],
  )

  const setVolume = useCallback(
    (v: number) => {
      const video = videoRef.current
      if (!video) return
      video.volume = v
      video.muted = v === 0
    },
    [videoRef],
  )

  const toggleMute = useCallback(() => {
    const video = videoRef.current
    if (!video) return
    video.muted = !video.muted
  }, [videoRef])

  const toggleFullscreen = useCallback(() => {
    const container = containerRef.current
    if (!container) return
    if (document.fullscreenElement) void document.exitFullscreen().catch(() => {})
    else if (container.requestFullscreen) void container.requestFullscreen().catch(() => {})
    else {
      // iPhone: no hay pantalla completa de elementos, solo la del <video>.
      const video = videoRef.current as
        | (HTMLVideoElement & { webkitEnterFullscreen?: () => void })
        | null
      video?.webkitEnterFullscreen?.()
    }
  }, [containerRef, videoRef])

  return {
    playing,
    ended,
    buffering,
    currentTime,
    duration: Math.max(duration, fallbackDuration),
    bufferedEnd,
    rate,
    volume,
    muted,
    fullscreen,
    toggle,
    seek,
    setVolume,
    toggleMute,
    toggleFullscreen,
  }
}
