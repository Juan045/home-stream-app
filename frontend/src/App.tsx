/**
 * Resolucion de los tres modos de entrada, iguales a los de static/player.html:
 *
 *   ?file=<ruta>     POST /api/v1/stream, crea sesion
 *   ?session=<id>    GET  /api/v1/sessions/{id}, retoma una existente
 *   ?src=<url>       modo CLI de transcode.py: sin API, sin sesion
 */
import { useEffect, useRef, useState } from 'react'
import {
  ApiError,
  errorMessage,
  openStream,
  readSession,
  type SubtitleTrack,
} from './api/client'
import { Player } from './components/Player'
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
        url: name ? base + 'subs/' + name : null,
      }
    })
  } catch {
    // Sin manifest se reproduce igual, solo sin subtitulos.
  }

  return source
}

export default function App() {
  const params = new URLSearchParams(location.search)
  const paramFile = params.get('file')
  const paramSession = params.get('session')
  const paramSrc = params.get('src')

  const { session, errorSlug, setSession, setErrorSlug } = useSession()
  const [cli, setCli] = useState<CliSource | null>(null)
  const started = useRef(false)

  useEffect(() => {
    // POST /stream no es idempotente: sin este guard, el doble montaje de
    // StrictMode abriria dos sesiones para el mismo asset.
    if (started.current) return
    started.current = true

    const fail = (err: unknown) => {
      setErrorSlug(err instanceof ApiError ? err.slug : 'unknown')
    }

    if (paramFile) {
      void openStream(paramFile).then(setSession).catch(fail)
    } else if (paramSession) {
      void readSession(paramSession).then(setSession).catch(fail)
    } else if (paramSrc) {
      void readCliManifest(paramSrc).then(setCli)
    }
  }, [paramFile, paramSession, paramSrc, setSession, setErrorSlug])

  // El modo ?src no tiene sesion: no corresponde heartbeat ni polling.
  useHeartbeat(session?.session_id ?? null)

  if (!paramFile && !paramSession && !paramSrc) {
    return (
      <div className="desktop">
        <div className="dialog" role="dialog" aria-label="Sin fuente configurada">
          <div className="bar">
            <span>Homeflix Classic</span>
          </div>
          <div className="body">
            <div className="notice">
              <div className="sign" aria-hidden="true">
                !
              </div>
              <div>
                <p className="text">
                  No hay ninguna fuente configurada. Abrir el player con uno de
                  estos parametros:
                </p>
                <code>?file=/ruta/absoluta/al/video.mkv</code>
                <code>?session=&lt;id de sesion&gt;</code>
                <code>?src=&lt;url de un master.m3u8&gt;</code>
              </div>
            </div>
          </div>
        </div>
      </div>
    )
  }

  if (cli) {
    return (
      <Player
        masterUrl={cli.masterUrl}
        title={cli.title}
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
      title={paramFile ? titleFromPath(paramFile) : ''}
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
