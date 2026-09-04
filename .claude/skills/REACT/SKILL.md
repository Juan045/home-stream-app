# React + TypeScript en Stream Media — Skill de implementación

Guía para construir el frontend de este proyecto con **React 19 + TypeScript + Vite**.
Cubre el andamiaje, la generación de tipos desde el backend, y sobre todo el punto
difícil: integrar **hls.js**, que es imperativo y con estado propio, dentro de un
árbol de componentes declarativo sin duplicar instancias ni perder la posición de
reproducción.

**Usar esta skill cuando:** se escriba o modifique código del frontend en `frontend/`.
Aplica a componentes, hooks, configuración de Vite y al contrato con la API.

**Alcance:** el **reproductor**, que hoy es la única funcionalidad del servicio. El
catálogo, el detalle de contenido y el panel de administración están previstos pero
**no definidos**: no anticipar su forma, no crear rutas para ellos, no agregar
dependencias que sólo ellos justificarían.

---

## Stack fijado

| Capa | Elección | Nota |
|---|---|---|
| Lenguaje | TypeScript, `strict: true` | Sin `any`. Los tipos de la API se generan, no se escriben. |
| Build | Vite | Salida estática servida por el `StaticFiles` de FastAPI. |
| UI | React 19 | `npm create vite@latest frontend -- --template react-ts` |
| HLS | `hls.js` desde npm, versión fijada | No CDN: el bundle se sirve del mismo origen. |

**Todavía no instalar** React Router ni TanStack Query. Con una sola pantalla, dos
hooks alcanzan. Entran cuando exista una segunda ruta real.

---

## Estructura

```
frontend/
├── index.html
├── vite.config.ts
├── tsconfig.json
└── src/
    ├── main.tsx
    ├── App.tsx
    ├── api/
    │   ├── types.ts        # GENERADO desde OpenAPI. No editar a mano.
    │   └── client.ts       # fetch tipado + normalizacion de errores
    ├── hooks/
    │   ├── useHlsPlayer.ts
    │   ├── useSession.ts
    │   └── useHeartbeat.ts
    └── components/
        ├── Player.tsx
        ├── AudioSelect.tsx
        ├── SubtitleSelect.tsx
        └── BuildProgress.tsx
```

---

## Configuración de Vite

El proxy evita CORS en desarrollo y hace que las URLs relativas que devuelve la API
(`master_url` es `/hls/{asset_id}/master.m3u8`) funcionen igual en dev y en producción.

```ts
// vite.config.ts
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:8000',
      '/hls': 'http://localhost:8000',
    },
  },
});
```

En producción el SPA se sirve desde el mismo origen que la API, así que no hay proxy
ni CORS. **El mount del `index.html` va último en `app/main.py`**, después del router
y del mount de `/hls` (`main.py:112-113`): Starlette resuelve en orden de registro.

---

## Tipos de la API

**Nunca escribir a mano los tipos de las respuestas.** Se generan desde el esquema
que ya publica FastAPI:

```
SM_DEBUG=1 uvicorn app.main:app      # el esquema solo se expone en debug
npx openapi-typescript http://localhost:8000/openapi.json -o src/api/types.ts
```

```ts
import type { components } from './types';

export type StreamResponse = components['schemas']['StreamResponse'];
export type AudioTrack     = components['schemas']['AudioTrackSchema'];
export type SubtitleTrack  = components['schemas']['SubtitleTrackSchema'];
```

Esta es la razón principal por la que el proyecto usa TypeScript: si cambia un campo
en `app/models/schemas.py`, el frontend deja de compilar. Regenerar los tipos después
de cada cambio de schema.

### Errores

El backend devuelve siempre `{"error": slug, "detail": texto}`. **Ramificar por
`error`, nunca por `detail`** — el slug es el contrato estable; el `detail` es prosa
y está siendo saneado (ver `SECURITY-HARDENING.md`).

```ts
export class ApiError extends Error {
  constructor(readonly slug: string, readonly status: number) {
    super(slug);
  }
}

export async function post<T>(url: string, body: unknown): Promise<T> {
  const resp = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!resp.ok) {
    const data = await resp.json().catch(() => ({ error: 'unknown' }));
    throw new ApiError(data.error ?? 'unknown', resp.status);
  }
  return resp.json() as Promise<T>;
}
```

Y **no renderizar `StreamResponse.error` crudo**: hoy contiene el stderr de FFmpeg
(F3 del plan de hardening). Mapear el valor a un mensaje propio y tratar lo
desconocido como "falló el procesamiento".

---

## hls.js dentro de React

Este es el núcleo de la skill. Casi todos los bugs del reproductor salen de acá.

### Las tres reglas

**1. La instancia de `Hls` va en un `useRef`, nunca en `useState`.**
No es dato de render: nadie la muestra. Ponerla en estado provoca un render por cada
cambio y abre la puerta a instancias duplicadas.

**2. Crear y destruir en el mismo `useEffect`, con `destroy()` en el cleanup.**
React 19 en `StrictMode` monta, desmonta y vuelve a montar cada efecto en desarrollo.
Sin `destroy()` en el cleanup quedan **dos instancias de hls.js sobre el mismo
`<video>`**, cada una pidiendo segmentos. Se manifiesta como descargas duplicadas y
audio que "vuelve solo" a la pista anterior. No sacar `StrictMode` para taparlo: el
doble montaje está revelando una fuga real.

**3. La única dependencia del efecto es la URL del master.**
Si entran el volumen, la pista elegida o cualquier estado de UI, el player se
reconstruye y **se pierde `currentTime`**. Ese es exactamente el defecto que la
arquitectura del backend existe para evitar.

### El hook

```ts
// hooks/useHlsPlayer.ts
import { useEffect, useRef, useState } from 'react';
import Hls from 'hls.js';

export interface HlsAudioTrack { id: number; label: string }

export function useHlsPlayer(src: string | null) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const hlsRef = useRef<Hls | null>(null);

  const [audioTracks, setAudioTracks] = useState<HlsAudioTrack[]>([]);
  const [currentAudio, setCurrentAudio] = useState(-1);
  const [complete, setComplete] = useState(false);

  useEffect(() => {
    const video = videoRef.current;
    if (!video || !src) return;

    // Safari reproduce HLS nativo y expone las renditions en video.audioTracks.
    if (!Hls.isSupported()) {
      video.src = src;
      return () => { video.removeAttribute('src'); video.load(); };
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
    });
    hlsRef.current = hls;

    hls.on(Hls.Events.AUDIO_TRACKS_UPDATED, (_e, data) => {
      setAudioTracks(
        data.audioTracks.map((t, i) => ({
          id: i,
          label: t.name || t.lang || `Pista ${i + 1}`,
        })),
      );
      setCurrentAudio(hls.audioTrack);
    });

    hls.on(Hls.Events.AUDIO_TRACK_SWITCHED, (_e, data) => setCurrentAudio(data.id));

    // EVENT -> VOD: cuando FFmpeg cierra la playlist, el video queda seekeable.
    hls.on(Hls.Events.LEVEL_LOADED, (_e, data) => setComplete(!data.details.live));

    hls.on(Hls.Events.ERROR, (_e, data) => {
      if (!data.fatal) return;
      switch (data.type) {
        case Hls.ErrorTypes.NETWORK_ERROR: hls.startLoad(); break;
        case Hls.ErrorTypes.MEDIA_ERROR:   hls.recoverMediaError(); break;
        default:                           hls.destroy(); break;
      }
    });

    hls.loadSource(src);
    hls.attachMedia(video);

    return () => {
      hls.destroy();
      hlsRef.current = null;
    };
  }, [src]);

  // Cambiar de idioma es esto y nada mas. No se toca el servidor.
  const selectAudio = (id: number) => {
    if (hlsRef.current) hlsRef.current.audioTrack = id;
    else if (videoRef.current) {
      const tracks = videoRef.current.audioTracks;
      for (let i = 0; i < tracks.length; i++) tracks[i].enabled = i === id;
    }
  };

  return { videoRef, audioTracks, currentAudio, selectAudio, complete };
}
```

### No configurar ABR

El master declara un solo `#EXT-X-STREAM-INF` (`app/services/playlist.py:192`).
`capLevelToPlayerSize`, `abrBandWidthFactor`, `startLevel` y cualquier selector de
calidad son ruido: no hay entre qué elegir. (El `player.html` actual trae
`capLevelToPlayerSize: true` por inercia; no replicarlo.)

---

## Audio: el cliente resuelve, el servidor no se entera

`hls.audioTrack = n`. Nada más. No se recarga el manifest, no se pierde
`currentTime`, no se reinicia FFmpeg, no se resetean los subtítulos.

> ⚠️ El ejemplo de la sección 5 de la skill **HLS** hace `POST /jobs/{id}/select`
> seguido de `loadHls(data.hls_url)` para cambiar de audio. Ese es el diseño anterior
> del proyecto, ya reemplazado. **No copiarlo.** Si aparece una llamada al servidor
> en el handler de cambio de idioma, está mal.

Corolario para React: el `<select>` de audio se alimenta de `audioTracks` y
`currentAudio`, que vienen de eventos de hls.js — no de un estado propio del
componente. La fuente de verdad es hls.js.

---

## Subtítulos

No están en el manifest: se sirven como `.vtt` estáticos y se cargan como `<track>`.
Por eso `hls.subtitleTracks` viene **vacío**; no usar esa API.

Dos particularidades que hay que respetar:

**Las URLs aparecen tarde.** Los subtítulos se extraen en paralelo con el video, así
que `subtitle_tracks[].url` puede ser `null` en las primeras respuestas y llenarse
después. La lista de opciones crece durante la reproducción — **sin pisar lo que el
usuario ya eligió**. Las pistas sin `url` se muestran deshabilitadas, no se ocultan.

**React no gestiona `textTrack.mode`.** Renderizar el `<track>` no lo activa. Hace
falta un efecto imperativo, y un `key` para que React reemplace el elemento en vez de
mutarle el `src`:

```tsx
<video ref={videoRef} controls playsInline>
  {subUrl && <track key={subUrl} kind="subtitles" src={subUrl} srcLang={lang} default />}
</video>
```

```ts
useEffect(() => {
  const video = videoRef.current;
  if (!video) return;
  const tracks = video.textTracks;
  for (let i = 0; i < tracks.length; i++) {
    tracks[i].mode = subUrl ? 'showing' : 'disabled';
  }
}, [subUrl]);
```

---

## Sesión: progreso y heartbeat

```ts
// hooks/useSession.ts — polling mientras el build corre
export function useSession(initial: StreamResponse | null) {
  const [session, setSession] = useState(initial);

  useEffect(() => {
    if (!session || session.status !== 'processing') return;
    const timer = setTimeout(async () => {
      setSession(await get<StreamResponse>(`/api/v1/sessions/${session.session_id}`));
    }, 2000);
    return () => clearTimeout(timer);
  }, [session]);

  return session;
}
```

```ts
// hooks/useHeartbeat.ts — sin heartbeat la sesion deja de proteger su asset del GC
export function useHeartbeat(sessionId: string | null) {
  useEffect(() => {
    if (!sessionId) return;
    const timer = setInterval(() => {
      fetch(`/api/v1/heartbeat/${sessionId}`, { method: 'POST' }).catch(() => {});
    }, 30_000);
    return () => clearInterval(timer);
  }, [sessionId]);
}
```

El `setTimeout` reprogramado por cambio de `session` evita solapamientos que un
`setInterval` sí produce si una respuesta tarda más que el intervalo.

### El gate de `playable`

**No cargar el master hasta que `playable` sea `true`.** El campo cuenta los
segmentos en disco, que aparecen mucho antes de que el build termine. Pedir el
manifest antes da 404 y arranca la máquina de reintentos sin necesidad.

```ts
const src = session?.playable ? session.master_url : null;
const player = useHlsPlayer(src);
```

Pasar `null` mientras no se puede reproducir es lo que hace que el efecto no corra:
por eso el hook acepta `string | null`.

---

## Modos de entrada

El player actual soporta tres, y hay que conservarlos:

| Query param | Comportamiento |
|---|---|
| `?file=<ruta>` | `POST /api/v1/stream` y crea sesión. |
| `?session=<id>` | `GET /api/v1/sessions/{id}`, retoma una sesión existente. |
| `?src=<url>` | Modo CLI de `transcode.py`: sin API. Lee `manifest.json` al lado del master para los subtítulos, y sigue si no está. |

`?src` no tiene sesión: no corresponde heartbeat ni polling.

**Nota:** el contrato de `?file` va a cambiar (`file_path` → identificador opaco, F1
del plan de hardening). Como los tipos se generan del OpenAPI, el cambio va a
aparecer como error de compilación. No adelantarlo.

---

## Convenciones

Las mismas que el backend, con la extensión obvia a TS:

- **Español** para comentarios y textos de UI. **Inglés** para identificadores.
- **Sin acentos en los comentarios del código** (los textos de UI sí los llevan).
- `strict: true`. Sin `any`; usar `unknown` y estrechar.
- Tipos de la API **importados de `api/types.ts`**, nunca redeclarados.
- Un hook por responsabilidad. Los componentes no llaman `fetch` directamente.

---

## Antipatrones

| No hacer | Por qué |
|---|---|
| `useState<Hls>` | No es dato de render; duplica instancias. |
| Crear `Hls` fuera del efecto | Sobrevive al desmontaje y filtra. |
| Volumen o pista en las deps del efecto | Reconstruye el player y pierde `currentTime`. |
| Llamar al servidor al cambiar de idioma | Es `hls.audioTrack = n`. |
| Quitar `StrictMode` porque "duplica" | Está revelando una fuga real de cleanup. |
| `currentTime` en estado de React | Un render por frame. Dejar el `<video>` no controlado. |
| Configurar ABR o selector de calidad | Hay un solo nivel de video. |
| Cargar hls.js por CDN | Va por npm, con versión fijada. |
| Escribir tipos de la API a mano | Se generan del OpenAPI. |
| Renderizar `response.error` crudo | Hoy es el stderr de FFmpeg. |
| Ramificar por `detail` | El contrato estable es `error`. |
| Cargar el master con `playable: false` | 404 y reintentos innecesarios. |

---

## Verificación manual

Los tests no cubren la reproducción real. Con un `.mkv` de varias pistas:

1. **Cambio de idioma:** reproducir hasta 02:00, cambiar audio → `currentTime` no se
   mueve, no hay corte de video y no aparecen procesos FFmpeg nuevos.
2. **StrictMode:** en dev, la pestaña Network muestra **una** descarga por segmento,
   no dos.
3. **Arranque en caliente:** abrir un archivo sin cachear → la barra de progreso
   aparece, la reproducción arranca en segundos y la lista sigue creciendo.
4. **EVENT → VOD:** al terminar el build, la barra de seek cubre todo el video.
5. **Subtítulos tardíos:** un idioma que aparece a mitad del build se suma al
   selector sin resetear la elección en curso.
6. **Desmontaje:** navegar fuera del player → cesan las descargas de segmentos.
