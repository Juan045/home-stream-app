# HLS en HTML — Skill de implementación

Guía completa para implementar reproducción de video HLS (HTTP Live Streaming) en HTML usando **hls.js** (standalone) y **Video.js Web Components** (`<hlsjs-video>`, `<hls-video>`, `<simple-hls-video>`, `<native-hls-video>`). Cubre desde el setup básico hasta configuración avanzada de ABR, DRM, live streaming y manejo de errores.

**Usar esta skill cuando:** se necesite implementar un reproductor de video HLS en una página HTML, ya sea con hls.js directamente o con los Web Components de Video.js. Aplica a reproductores embebidos, páginas de streaming, dashboards de video, y cualquier interfaz que reproduzca contenido `.m3u8`.

---

## Decisión de enfoque

Antes de escribir código, elegir el enfoque según las necesidades:

| Enfoque | Cuándo usarlo | Bundle size |
|---|---|---|
| **hls.js standalone** | Control total sobre el reproductor, UI custom, integración con frameworks propios | ~70 KB gzip |
| **`<hlsjs-video>`** | Todas las features de hls.js como Web Component, configuración avanzada del engine | Mayor |
| **`<hls-video>`** | Bundle mínimo, funcionalidad HLS básica suficiente | Menor |
| **`<simple-hls-video>`** | Mínimo absoluto, solo reproducción sin configuración avanzada | Mínimo |
| **`<native-hls-video>`** | Solo Safari / iOS, sin dependencia de hls.js | Sin hls.js |

**Regla general:** usar hls.js standalone cuando se necesita un reproductor custom con UI propia. Usar `<hlsjs-video>` cuando se quiere el poder de hls.js con la ergonomía de un Web Component. Usar `<native-hls-video>` solo si el target es exclusivamente Safari/iOS.

---

## 1. hls.js standalone

### 1.1 Instalación

```bash
npm install hls.js
```

O vía CDN:

```html
<script src="https://cdn.jsdelivr.net/npm/hls.js@latest"></script>
```

### 1.2 Setup básico

```html
<video id="video" controls></video>

<script>
  const video = document.getElementById('video');
  const src = 'https://example.com/stream/master.m3u8';

  if (Hls.isSupported()) {
    const hls = new Hls();
    hls.loadSource(src);
    hls.attachMedia(video);
    hls.on(Hls.Events.MANIFEST_PARSED, () => {
      video.play();
    });
  } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
    // Safari nativo
    video.src = src;
    video.addEventListener('loadedmetadata', () => {
      video.play();
    });
  } else {
    console.error('HLS no soportado en este navegador');
  }
</script>
```

IMPORTANTE: siempre verificar `Hls.isSupported()` primero. Safari soporta HLS nativamente sin hls.js — usar el fallback con `canPlayType`.

### 1.3 Configuración recomendada

```javascript
const hls = new Hls({
  // Performance
  enableWorker: true,              // Web Worker para demuxing (recomendado)
  lowLatencyMode: false,           // true solo para live de baja latencia

  // Buffer
  maxBufferLength: 30,             // Segundos máximos de buffer adelante
  maxMaxBufferLength: 600,         // Límite absoluto de buffer
  maxBufferSize: 60 * 1000 * 1000, // 60 MB límite de tamaño de buffer
  maxBufferHole: 0.5,              // Agujero máximo tolerado en el buffer (segundos)
  backBufferLength: 90,            // Segundos de buffer atrás (para seek hacia atrás)

  // ABR (Adaptive Bitrate)
  abrEwmaDefaultEstimate: 500000,  // Estimación inicial de bandwidth (bps)
  abrBandWidthFactor: 0.95,        // Factor conservador para bajar de calidad
  abrBandWidthUpFactor: 0.7,       // Factor para subir de calidad (más agresivo)
  abrMaxWithRealBitrate: false,    // true = usar bitrate real vs estimado del manifest
  startLevel: -1,                  // -1 = auto, 0+ = nivel específico
  capLevelToPlayerSize: true,      // No cargar resolución mayor al tamaño del player

  // Loading y reintentos
  manifestLoadingTimeOut: 10000,   // Timeout para cargar manifest (ms)
  manifestLoadingMaxRetry: 3,      // Reintentos para manifest
  manifestLoadingRetryDelay: 1000, // Delay entre reintentos (ms)
  fragLoadingTimeOut: 20000,       // Timeout para cargar fragmento (ms)
  fragLoadingMaxRetry: 6,          // Reintentos para fragmentos
  fragLoadingRetryDelay: 1000,     // Delay entre reintentos (ms)

  // Debug
  debug: false,                    // true para logging detallado en consola
});
```

### 1.4 Manejo de errores

```javascript
hls.on(Hls.Events.ERROR, (event, data) => {
  if (data.fatal) {
    switch (data.type) {
      case Hls.ErrorTypes.NETWORK_ERROR:
        // Error de red: reintentar carga
        console.error('Error de red fatal:', data.details);
        hls.startLoad();
        break;

      case Hls.ErrorTypes.MEDIA_ERROR:
        // Error de media: intentar recuperación
        console.error('Error de media fatal:', data.details);
        hls.recoverMediaError();
        break;

      default:
        // Error irrecuperable: destruir instancia
        console.error('Error irrecuperable:', data.details);
        hls.destroy();
        break;
    }
  } else {
    // Errores no fatales — hls.js los maneja internamente
    console.warn('Error no fatal:', data.type, data.details);
  }
});
```

Errores comunes por `data.details`:
- `Hls.ErrorDetails.MANIFEST_LOAD_ERROR` — no se pudo cargar el .m3u8
- `Hls.ErrorDetails.MANIFEST_LOAD_TIMEOUT` — timeout al cargar manifest
- `Hls.ErrorDetails.FRAG_LOAD_ERROR` — no se pudo cargar un segmento .ts
- `Hls.ErrorDetails.BUFFER_APPEND_ERROR` — error al escribir en el buffer

### 1.5 Control de calidad (ABR)

```javascript
// Listar niveles de calidad disponibles
hls.on(Hls.Events.MANIFEST_PARSED, (event, data) => {
  console.log('Niveles disponibles:', data.levels.length);
  hls.levels.forEach((level, i) => {
    console.log(`  Nivel ${i}: ${level.width}x${level.height} @ ${Math.round(level.bitrate / 1000)} kbps`);
  });
});

// Escuchar cambios de calidad
hls.on(Hls.Events.LEVEL_SWITCHED, (event, data) => {
  const level = hls.levels[data.level];
  console.log(`Cambio a: ${level.width}x${level.height}`);
});

// Modo automático (ABR)
hls.currentLevel = -1;

// Forzar nivel específico
hls.currentLevel = 2;  // Índice del nivel

// Fijar nivel máximo
hls.autoLevelCapping = 3;  // No subir por encima del nivel 3

// Obtener nivel actual
console.log('Nivel actual:', hls.currentLevel);
console.log('Nivel auto sugerido:', hls.autoLevel);
```

### 1.6 Eventos principales

```javascript
// Ciclo de vida
hls.on(Hls.Events.MEDIA_ATTACHED, () => { /* video element vinculado */ });
hls.on(Hls.Events.MANIFEST_PARSED, (e, data) => { /* manifest cargado, data.levels disponible */ });
hls.on(Hls.Events.LEVEL_LOADED, (e, data) => { /* nivel de calidad cargado */ });
hls.on(Hls.Events.FRAG_LOADED, (e, data) => { /* fragmento descargado, data.frag.sn = número de secuencia */ });
hls.on(Hls.Events.FRAG_BUFFERED, (e, data) => { /* fragmento procesado y en buffer */ });

// Calidad
hls.on(Hls.Events.LEVEL_SWITCHING, (e, data) => { /* cambiando de nivel */ });
hls.on(Hls.Events.LEVEL_SWITCHED, (e, data) => { /* nivel cambiado */ });

// Buffer
hls.on(Hls.Events.BUFFER_CREATED, () => { /* SourceBuffer creado */ });
hls.on(Hls.Events.BUFFER_APPENDED, () => { /* datos agregados al buffer */ });

// Audio / Subtítulos
hls.on(Hls.Events.AUDIO_TRACKS_UPDATED, (e, data) => { /* pistas de audio disponibles */ });
hls.on(Hls.Events.AUDIO_TRACK_SWITCHED, (e, data) => { /* pista de audio cambiada */ });
hls.on(Hls.Events.SUBTITLE_TRACKS_UPDATED, (e, data) => { /* subtítulos disponibles */ });
hls.on(Hls.Events.SUBTITLE_TRACK_SWITCH, (e, data) => { /* subtítulo cambiado */ });

// Errores
hls.on(Hls.Events.ERROR, (e, data) => { /* ver sección 1.4 */ });
```

### 1.7 Pistas de audio y subtítulos

```javascript
// Audio
console.log('Pistas de audio:', hls.audioTracks);
hls.audioTrack = 1;  // Cambiar a segunda pista de audio

// Subtítulos
console.log('Pistas de subtítulos:', hls.subtitleTracks);
hls.subtitleTrack = 0;    // Activar primer subtítulo
hls.subtitleTrack = -1;   // Desactivar subtítulos
hls.subtitleDisplay = true;  // Mostrar/ocultar subtítulos
```

### 1.8 Destrucción y limpieza

```javascript
// Siempre destruir al desmontar o cambiar de fuente
hls.destroy();

// Para cambiar de fuente sin destruir:
hls.loadSource('https://example.com/otro.m3u8');
```

### 1.9 CORS

El servidor que sirve los archivos `.m3u8` y `.ts` debe incluir headers CORS:

```
Access-Control-Allow-Origin: *
Access-Control-Allow-Headers: Content-Type, Range
Access-Control-Expose-Headers: Content-Length, Content-Range
```

Si se usan credenciales:

```javascript
const hls = new Hls({
  xhrSetup: (xhr, url) => {
    xhr.withCredentials = true;
  }
});
```

O con fetch:

```javascript
const hls = new Hls({
  fetchSetup: (context, initParams) => {
    initParams.credentials = 'include';
    return new Request(context.url, initParams);
  }
});
```

---

## 2. Video.js Web Components

### 2.1 Instalación

```bash
npm install @anthropic/videojs
```

Los componentes se registran automáticamente como Custom Elements al importar.

### 2.2 `<hlsjs-video>` — Completo

El componente más potente. Usa hls.js internamente con acceso completo a su configuración.

```html
<script type="module">
  import '@anthropic/videojs/media/hlsjs-video';
</script>

<hlsjs-video
  id="player"
  src="https://example.com/stream/master.m3u8"
  controls
  autoplay
  muted
  playsinline
  poster="thumbnail.jpg"
  preload="metadata"
></hlsjs-video>
```

#### Configuración avanzada vía JavaScript

```javascript
const video = document.querySelector('#player');

video.source = {
  src: 'https://example.com/stream/master.m3u8',
  engine: {
    hlsJs: {
      maxBufferLength: 30,
      lowLatencyMode: false,
      capLevelToPlayerSize: true,
      enableWorker: true,
      startLevel: -1,
      debug: false,

      // ABR
      abrBandWidthFactor: 0.95,
      abrBandWidthUpFactor: 0.7,
      abrEwmaDefaultEstimate: 500000,

      // Loading
      fragLoadingTimeOut: 20000,
      fragLoadingMaxRetry: 6,
      manifestLoadingTimeOut: 10000,
      manifestLoadingMaxRetry: 3,

      // FPS monitoring
      capLevelOnFPSDrop: true,
      fpsDroppedMonitoringPeriod: 5000,
      fpsDroppedMonitoringThreshold: 0.3,
    }
  }
};
```

#### Propiedades clave

```javascript
const video = document.querySelector('#player');

// Estado de reproducción
video.currentTime;    // Posición actual (segundos)
video.duration;       // Duración total
video.paused;         // ¿Está pausado?
video.playing;        // ¿Está reproduciendo?
video.playbackRate;   // Velocidad (1.0 = normal)
video.volume;         // Volumen (0.0 a 1.0)
video.muted;          // ¿Silenciado?

// Dimensiones
video.videoWidth;     // Ancho del video
video.videoHeight;    // Alto del video

// HLS específico
video.streamType;     // Tipo de stream ('unknown', 'on-demand', 'live')
video.engine;         // Instancia hls.js interna (Hls | null)
video.source;         // Fuente configurada
video.error;          // Error actual (MediaError | null)

// Pistas
video.audioTracks;    // AudioTrackList
video.videoTracks;    // VideoTrackList
video.textTracks;     // TextTrackList
video.audioRenditions;  // Rendiciones de audio disponibles
video.videoRenditions;  // Rendiciones de video disponibles

// Picture-in-Picture / Fullscreen
video.isFullscreen;
video.isPictureInPicture;

// Live
video.liveEdgeStart;
video.targetLiveWindow;
```

#### Eventos

```javascript
const video = document.querySelector('#player');

// Reproducción
video.addEventListener('play', () => {});
video.addEventListener('pause', () => {});
video.addEventListener('playing', () => {});
video.addEventListener('ended', () => {});
video.addEventListener('timeupdate', () => {});
video.addEventListener('seeking', () => {});
video.addEventListener('seeked', () => {});
video.addEventListener('waiting', () => {});

// Carga
video.addEventListener('loadstart', () => {});
video.addEventListener('loadedmetadata', () => {});
video.addEventListener('loadeddata', () => {});
video.addEventListener('canplay', () => {});
video.addEventListener('canplaythrough', () => {});
video.addEventListener('progress', () => {});

// Video.js específicos
video.addEventListener('sourcechange', () => {
  console.log('Nueva fuente:', video.source);
});
video.addEventListener('streamtypechange', () => {
  console.log('Tipo de stream:', video.streamType);
});
video.addEventListener('targetlivewindowchange', () => {
  console.log('Ventana live:', video.targetLiveWindow);
});

// Errores
video.addEventListener('error', () => {
  console.error('Error:', video.error);
});
```

### 2.3 `<hls-video>` — Ligero

Mismo API que `<hlsjs-video>` pero optimizado para bundle mínimo. Sin acceso completo a configuración del engine hls.js.

```html
<script type="module">
  import '@anthropic/videojs/media/hls-video';
</script>

<hls-video
  src="https://example.com/stream/master.m3u8"
  controls
  autoplay
  muted
  playsinline
></hls-video>
```

Usar cuando: se necesita HLS básico sin configuración avanzada del engine, y el tamaño del bundle importa.

### 2.4 `<simple-hls-video>` — Mínimo

```html
<script type="module">
  import '@anthropic/videojs/media/simple-hls-video';
</script>

<simple-hls-video
  src="https://example.com/stream/master.m3u8"
  controls
  autoplay
  muted
  playsinline
></simple-hls-video>
```

Usar cuando: solo se necesita reproducir un stream HLS sin ninguna configuración especial.

### 2.5 `<native-hls-video>` — Solo Safari

```html
<script type="module">
  import '@anthropic/videojs/media/native-hls-video';
</script>

<native-hls-video
  src="https://example.com/stream/master.m3u8"
  controls
  autoplay
  muted
  playsinline
></native-hls-video>
```

Usar cuando: el target es exclusivamente Safari/iOS y no se quiere incluir hls.js en el bundle.

#### DRM con native-hls-video

```javascript
const video = document.querySelector('native-hls-video');
video.source = {
  src: 'https://example.com/protected.m3u8',
  engine: {
    nativeHls: {
      drmSystems: {
        'com.apple.fps.1_0': {
          licenseUrl: 'https://license-server.com/fairplay',
          certificateUrl: 'https://license-server.com/certificate'
        }
      }
    }
  }
};
```

### 2.6 CSS customization (todos los componentes)

```css
hlsjs-video,
hls-video,
simple-hls-video,
native-hls-video {
  --media-video-border-radius: 8px;
  --media-object-fit: contain;        /* cover | contain | fill */
  --media-object-position: center;
  
  /* Subtítulos */
  --media-caption-track-duration: 0.3s;
  --media-caption-track-delay: 0s;
  --media-caption-track-y: 80%;

  width: 100%;
  max-width: 800px;
  aspect-ratio: 16/9;
}
```

---

## 3. Configuración para Live Streaming

### 3.1 hls.js standalone — Live

```javascript
const hls = new Hls({
  lowLatencyMode: true,
  liveSyncDurationCount: 3,
  liveMaxLatencyDurationCount: 10,
  liveSyncMode: 'edge',
  maxLiveSyncPlaybackRate: 1.5,   // Acelerar para alcanzar el edge
  liveDurationInfinity: true,
  backBufferLength: 30,           // Reducir para live
  maxBufferLength: 10,            // Buffer más corto para live
});
```

### 3.2 `<hlsjs-video>` — Live

```javascript
const video = document.querySelector('hlsjs-video');
video.source = {
  src: 'https://example.com/live.m3u8',
  engine: {
    hlsJs: {
      lowLatencyMode: true,
      liveSyncDurationCount: 2,
      liveSyncMode: 'edge',
      liveMaxLatencyDuration: 10,
      maxLiveSyncPlaybackRate: 1.5,
      autoStartLoad: true,
    }
  }
};
```

---

## 4. DRM (Digital Rights Management)

### 4.1 Widevine + hls.js

```javascript
const hls = new Hls({
  emeEnabled: true,
  drmSystems: {
    'com.widevine.alpha': {
      licenseUrl: 'https://license-server.com/widevine'
    }
  },
  licenseXhrSetup: (xhr, url) => {
    xhr.setRequestHeader('Authorization', 'Bearer <token>');
  }
});
```

### 4.2 Widevine + `<hlsjs-video>`

```javascript
const video = document.querySelector('hlsjs-video');
video.source = {
  src: 'https://example.com/protected.m3u8',
  engine: {
    hlsJs: {
      emeEnabled: true,
      drmSystems: {
        'com.widevine.alpha': {
          licenseUrl: 'https://license-server.com/widevine'
        }
      },
      licenseXhrSetup: (xhr, url) => {
        xhr.setRequestHeader('Authorization', 'Bearer <token>');
      }
    }
  }
};
```

---

## 5. Ejemplo completo: Reproductor con selector de audio y subtítulos

Este es el patrón para un reproductor embebido que consume HLS con selección de pistas de audio y subtítulos desde un backend propio (como Stream Media).

```html
<!DOCTYPE html>
<html lang="es">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Reproductor HLS</title>
  <script src="https://cdn.jsdelivr.net/npm/hls.js@latest"></script>
  <style>
    * { box-sizing: border-box; margin: 0; }
    body { background: #111; color: #eee; font-family: system-ui, sans-serif; }
    .player-container {
      max-width: 960px;
      margin: 2rem auto;
    }
    video {
      width: 100%;
      aspect-ratio: 16/9;
      background: #000;
      border-radius: 8px;
    }
    .controls {
      display: flex;
      gap: 1rem;
      padding: 1rem 0;
      flex-wrap: wrap;
    }
    select {
      padding: 0.5rem;
      border-radius: 4px;
      background: #222;
      color: #eee;
      border: 1px solid #444;
      font-size: 0.875rem;
    }
  </style>
</head>
<body>
  <div class="player-container">
    <video id="video" controls playsinline></video>
    <div class="controls">
      <label>
        Audio:
        <select id="audio-select"></select>
      </label>
      <label>
        Subtítulos:
        <select id="sub-select">
          <option value="-1">Desactivados</option>
        </select>
      </label>
    </div>
  </div>

  <script>
    const video = document.getElementById('video');
    const audioSelect = document.getElementById('audio-select');
    const subSelect = document.getElementById('sub-select');

    const API_BASE = '/api/v1';
    let jobId = null;
    let hls = null;

    // Inicializar reproductor
    async function initPlayer(jobData) {
      jobId = jobData.job_id;

      // Poblar selectores
      jobData.audio_tracks.forEach((track, i) => {
        const opt = document.createElement('option');
        opt.value = track.index;
        opt.textContent = `${track.title || track.language} (${track.codec}, ${track.channels}ch)`;
        if (i === jobData.current_audio_track) opt.selected = true;
        audioSelect.appendChild(opt);
      });

      jobData.subtitle_tracks.forEach((track) => {
        const opt = document.createElement('option');
        opt.value = track.index;
        opt.textContent = `${track.title || track.language} (${track.format})`;
        subSelect.appendChild(opt);
      });

      // Inicializar hls.js
      loadHls(jobData.hls_url);

      // Heartbeat cada 30 segundos
      setInterval(() => {
        fetch(`${API_BASE}/heartbeat/${jobId}`, { method: 'POST' });
      }, 30000);
    }

    function loadHls(url) {
      if (hls) {
        hls.destroy();
      }

      if (Hls.isSupported()) {
        hls = new Hls({
          enableWorker: true,
          maxBufferLength: 30,
          maxBufferHole: 0.5,
          fragLoadingMaxRetry: 6,
          capLevelToPlayerSize: true,
        });

        hls.loadSource(url);
        hls.attachMedia(video);

        hls.on(Hls.Events.MANIFEST_PARSED, () => {
          video.play().catch(() => {});
        });

        hls.on(Hls.Events.ERROR, (event, data) => {
          if (data.fatal) {
            switch (data.type) {
              case Hls.ErrorTypes.NETWORK_ERROR:
                hls.startLoad();
                break;
              case Hls.ErrorTypes.MEDIA_ERROR:
                hls.recoverMediaError();
                break;
              default:
                hls.destroy();
                break;
            }
          }
        });
      } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
        video.src = url;
      }
    }

    // Cambio de pista de audio
    audioSelect.addEventListener('change', async (e) => {
      const currentTime = video.currentTime;
      const res = await fetch(`${API_BASE}/jobs/${jobId}/select`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ audio_track: parseInt(e.target.value) })
      });
      const data = await res.json();

      // Recargar HLS con nueva URL (el backend reinició FFmpeg)
      loadHls(data.hls_url);
    });

    // Cambio de subtítulos
    subSelect.addEventListener('change', async (e) => {
      const trackIndex = parseInt(e.target.value);

      // Remover tracks existentes
      while (video.textTracks.length > 0) {
        const track = video.textTracks[0];
        track.mode = 'disabled';
      }
      const existingTracks = video.querySelectorAll('track');
      existingTracks.forEach(t => t.remove());

      if (trackIndex === -1) return; // Desactivados

      const res = await fetch(`${API_BASE}/jobs/${jobId}/select`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ subtitle_track: trackIndex })
      });
      const data = await res.json();

      if (data.subtitle_url) {
        const track = document.createElement('track');
        track.kind = 'subtitles';
        track.src = data.subtitle_url;
        track.default = true;
        video.appendChild(track);
        track.track.mode = 'showing';
      }
    });
  </script>
</body>
</html>
```

---

## 6. Patrones importantes

### 6.1 Verificación de soporte

SIEMPRE verificar soporte antes de inicializar:

```javascript
if (Hls.isSupported()) {
  // Usar hls.js — funciona en Chrome, Firefox, Edge, etc.
} else if (video.canPlayType('application/vnd.apple.mpegurl')) {
  // Safari — HLS nativo, asignar src directamente
} else {
  // Sin soporte HLS — mostrar mensaje o fallback
}
```

### 6.2 Limpieza de recursos

SIEMPRE destruir la instancia al salir o cambiar de fuente:

```javascript
// Al desmontar componente o cambiar de página
window.addEventListener('beforeunload', () => {
  if (hls) {
    hls.destroy();
    hls = null;
  }
});
```

### 6.3 Autoplay

Los navegadores bloquean autoplay con audio. Usar `muted` para autoplay garantizado:

```html
<video autoplay muted playsinline></video>
```

Si se quiere autoplay con audio, intentar y hacer fallback a muted:

```javascript
video.play().catch(() => {
  video.muted = true;
  video.play();
});
```

### 6.4 Responsive

```css
video {
  width: 100%;
  max-width: 100%;
  height: auto;
  aspect-ratio: 16/9;
}
```

### 6.5 Subtítulos WebVTT

Para cargar subtítulos externos (no embebidos en el HLS):

```html
<video id="video" controls>
  <track kind="subtitles" src="/subs/es.vtt" srclang="es" label="Español">
  <track kind="subtitles" src="/subs/en.vtt" srclang="en" label="English">
</video>
```

Control programático:

```javascript
const tracks = video.textTracks;
for (let i = 0; i < tracks.length; i++) {
  tracks[i].mode = (i === selectedIndex) ? 'showing' : 'disabled';
}
```

### 6.6 Poster y loading state

```html
<video
  poster="/thumbnails/preview.jpg"
  preload="metadata"
></video>
```

### 6.7 Múltiples calidades sin ABR (calidad manual)

Cuando el backend genera un solo nivel (sin ABR), manejar la calidad desde el cliente:

```javascript
// Si hay múltiples niveles en el manifest
hls.on(Hls.Events.MANIFEST_PARSED, (event, data) => {
  if (data.levels.length > 1) {
    // Construir selector de calidad
    data.levels.forEach((level, i) => {
      // Agregar opción: `${level.height}p`
    });
  }
});

// Cambiar calidad manualmente
function setQuality(levelIndex) {
  hls.currentLevel = levelIndex; // -1 para auto
}
```

---

## 7. Referencia rápida de configuración hls.js

| Opción | Tipo | Default | Descripción |
|---|---|---|---|
| `enableWorker` | boolean | true | Web Worker para demuxing |
| `lowLatencyMode` | boolean | false | Modo baja latencia para live |
| `maxBufferLength` | number | 30 | Máximo buffer adelante (s) |
| `maxBufferSize` | number | 60MB | Máximo tamaño de buffer |
| `backBufferLength` | number | Infinity | Buffer atrás (s) |
| `startLevel` | number | -1 | Nivel inicial (-1=auto) |
| `capLevelToPlayerSize` | boolean | false | Limitar resolución al player |
| `debug` | boolean | false | Logging en consola |
| `fragLoadingTimeOut` | number | 20000 | Timeout de fragmento (ms) |
| `fragLoadingMaxRetry` | number | 6 | Reintentos de fragmento |
| `manifestLoadingTimeOut` | number | 10000 | Timeout de manifest (ms) |
| `manifestLoadingMaxRetry` | number | 1 | Reintentos de manifest |
| `abrBandWidthFactor` | number | 0.95 | Factor ABR para bajar |
| `abrBandWidthUpFactor` | number | 0.7 | Factor ABR para subir |
| `liveSyncDurationCount` | number | 3 | Segmentos de sync para live |
| `liveMaxLatencyDurationCount` | number | Infinity | Latencia máxima (segmentos) |

---

## 8. Troubleshooting

**El video no carga:**
1. Verificar que la URL del .m3u8 es accesible (probar con curl)
2. Verificar headers CORS en el servidor
3. Activar `debug: true` en hls.js para ver errores detallados
4. Verificar que los segmentos .ts son accesibles desde el mismo origen

**Buffer stalls (se para la reproducción):**
1. Aumentar `maxBufferLength`
2. Aumentar `fragLoadingMaxRetry`
3. Verificar ancho de banda del servidor
4. Verificar que los segmentos se generan más rápido que el tiempo real

**Error MEDIA_ERROR frecuente:**
1. Verificar que los segmentos .ts están correctamente codificados
2. Verificar que los keyframes están alineados con los segmentos
3. Usar `hls.recoverMediaError()` como primera línea de recuperación

**Seek no funciona correctamente:**
1. Verificar que el manifest tiene `#EXT-X-ENDLIST` (para VOD)
2. Verificar que los segmentos tienen keyframes al inicio
3. Verificar numeración correcta con `-start_number`

**Subtítulos no se muestran:**
1. Verificar formato WebVTT válido (empieza con `WEBVTT`)
2. Verificar que `track.mode = 'showing'`
3. Verificar CORS para el archivo .vtt