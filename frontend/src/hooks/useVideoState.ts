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
  buffering: boolean
  currentTime: number
  duration: number
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
  const [buffering, setBuffering] = useState(false)
  const [currentTime, setCurrentTime] = useState(0)
  const [duration, setDuration] = useState(0)
  const [volume, setVolumeState] = useState(1)
  const [muted, setMuted] = useState(false)
  const [fullscreen, setFullscreen] = useState(false)

  useEffect(() => {
    const video = videoRef.current
    if (!video) return

    const onPlay = () => setPlaying(true)
    const onPause = () => setPlaying(false)
    const onWaiting = () => setBuffering(true)
    const onPlaying = () => setBuffering(false)
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
    video.addEventListener('waiting', onWaiting)
    video.addEventListener('playing', onPlaying)
    video.addEventListener('timeupdate', onTime)
    video.addEventListener('durationchange', onDuration)
    video.addEventListener('volumechange', onVolume)

    return () => {
      video.removeEventListener('play', onPlay)
      video.removeEventListener('pause', onPause)
      video.removeEventListener('waiting', onWaiting)
      video.removeEventListener('playing', onPlaying)
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
    else void container.requestFullscreen().catch(() => {})
  }, [containerRef])

  return {
    playing,
    buffering,
    currentTime,
    duration: Math.max(duration, fallbackDuration),
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
