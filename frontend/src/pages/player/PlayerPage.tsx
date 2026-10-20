/**
 * Resolucion de los cuatro modos de entrada:
 *
 *   ?media=<id>      POST /api/v1/stream {id_media}, crea sesion — el de la galeria
 *   ?file=<ruta>     POST /api/v1/stream {file_path}, ruta absoluta
 *   ?session=<id>    GET  /api/v1/sessions/{id}, retoma una existente
 *   ?src=<url>       modo CLI de transcode.py: sin API, sin sesion
 */
import { useEffect, useRef, useState } from 'react'
import {
  ApiError,
  errorMessage,
  openStreamByMedia,
  openStreamByPath,
  readMedia,
  readSession,
  type SubtitleTrack,
} from '../../api/client'
import { KINDS } from '../../format'
import { Player } from './Player'
import { useHeartbeat } from './hooks/useHeartbeat'
import { useSession } from './hooks/useSession'

interface CliSource {
  masterUrl: string
  title: string
  duration: number
  subtitles: SubtitleTrack[]
}

interface CliManifest {
  source?: string
  duration?: number
  subtitles?: Record<string, string>
  info?: {
    subtitle_tracks?: {
      index: number
      codec: string
      language: string
      title: string
      // Opcional: los manifests escritos antes de que existiera el flag no lo
      // traen, y el modo CLI lee el archivo tal como quedo en disco.
      ignore?: boolean
    }[]
  }
}

function titleFromPath(path: string): string {
  const base = path.split(/[\\/]/).pop() ?? path
  return base.replace(/\.[^.]+$/, '')
}

/** Modo CLI: no hay API, pero el manifest del asset esta al lado del master. */
async function readCliManifest(url: string): Promise<CliSource> {
  const base = url.substring(0, url.lastIndexOf('/') + 1)
  const source: CliSource = {
    masterUrl: url,
    title: titleFromPath(url),
    duration: 0,
    subtitles: [],
  }

  try {
    const resp = await fetch(base + 'manifest.json')
    if (!resp.ok) return source
    const manifest = (await resp.json()) as CliManifest
    if (manifest.source) source.title = titleFromPath(manifest.source)
    if (manifest.duration) source.duration = manifest.duration
    source.subtitles = (manifest.info?.subtitle_tracks ?? []).map((track) => {
      const name = manifest.subtitles?.[String(track.index)]
      return {
        index: track.index,
        codec: track.codec,
        language: track.language,
        title: track.title,
        ignore: track.ignore ?? false,
        url: name ? base + 'subs/' + name : null,
      }
    })
  } catch {
    // Sin manifest se reproduce igual, solo sin subtitulos.
  }

  return source
}

export default function PlayerPage() {
  const params = new URLSearchParams(location.search)
  const paramMedia = params.get('media')
  const paramFile = params.get('file')
  const paramSession = params.get('session')
  const paramSrc = params.get('src')

  // Si esta lista se desincroniza del `if` de abajo, el efecto abre la sesion
  // pero el player no se renderiza nunca: la sesion queda colgada y no se pide
  // ningun segmento. Agregar un modo es tocar los dos lugares.
  const hasSource = Boolean(paramMedia ?? paramFile ?? paramSession ?? paramSrc)

  const { session, errorSlug, setSession, setErrorSlug } = useSession()
  const [cli, setCli] = useState<CliSource | null>(null)
  // El titulo editorial no viaja en StreamResponse: se pide la ficha aparte.
  const [mediaTitle, setMediaTitle] = useState('')
  // "Película · 2010", la linea de abajo del titulo.
  const [mediaMeta, setMediaMeta] = useState('')
  const started = useRef(false)

  useEffect(() => {
    // POST /stream no es idempotente: sin este guard, el doble montaje de
    // StrictMode abriria dos sesiones para el mismo asset.
    if (started.current) return
    started.current = true

    const fail = (err: unknown) => {
      setErrorSlug(err instanceof ApiError ? err.slug : 'unknown')
    }

    if (paramMedia) {
      void openStreamByMedia(paramMedia).then(setSession).catch(fail)
      // Solo para la barra de arriba: si falla, se reproduce igual y sin titulo.
      void readMedia(paramMedia)
        .then((media) => {
          setMediaTitle(media.title)
          setMediaMeta([KINDS[media.kind] ?? media.kind, media.year].filter(Boolean).join(' · '))
        })
        .catch(() => {})
    } else if (paramFile) {
      void openStreamByPath(paramFile).then(setSession).catch(fail)
    } else if (paramSession) {
      void readSession(paramSession).then(setSession).catch(fail)
    } else if (paramSrc) {
      void readCliManifest(paramSrc).then(setCli)
    }
  }, [paramMedia, paramFile, paramSession, paramSrc, setSession, setErrorSlug])

  // El modo ?src no tiene sesion: no corresponde heartbeat ni polling.
  useHeartbeat(session?.session_id ?? null)

  if (!hasSource) {
    return (
      <Player
        noSource
        masterUrl={null}
        title=""
        meta=""
        strategy={null}
        subtitles={[]}
        duration={0}
        buildProgress={null}
        apiError={null}
        apiErrorCode={null}
      />
    )
  }

  if (cli) {
    return (
      <Player
        masterUrl={cli.masterUrl}
        title={cli.title}
        meta=""
        strategy={null}
        subtitles={cli.subtitles}
        duration={cli.duration}
        buildProgress={null}
        apiError={null}
        apiErrorCode={null}
      />
    )
  }

  // El gate de playable: pedir el master antes da 404 y arranca la maquina de
  // reintentos sin necesidad. Los segmentos aparecen mucho antes del final.
  const masterUrl = session?.playable ? session.master_url : null
  const failed = session?.status === 'failed'

  let apiError: string | null = null
  let apiErrorCode: string | null = null
  if (errorSlug) {
    apiError = errorMessage(errorSlug)
    apiErrorCode = errorSlug
  } else if (failed) {
    // session.error trae hoy el stderr crudo de FFmpeg: no se muestra.
    apiError = 'Fallo el procesamiento del video.'
    apiErrorCode = 'build_failed'
  }

  return (
    <Player
      masterUrl={masterUrl}
      title={mediaTitle || (paramFile ? titleFromPath(paramFile) : '')}
      meta={mediaMeta}
      strategy={session?.strategy ?? null}
      subtitles={session?.subtitle_tracks ?? []}
      duration={session?.duration_seconds ?? 0}
      buildProgress={
        session && session.status === 'processing' ? session.progress : null
      }
      apiError={apiError}
      apiErrorCode={apiErrorCode}
    />
  )
}
