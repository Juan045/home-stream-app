# Stream Media

Servicio auto-hospedado de streaming multimedia. Recibe una ruta de archivo de video, detecta codec, pistas de audio y subtítulos, y lo sirve al navegador vía HLS con selección de pistas de audio.

## Documento de diseño

El archivo `stream-media-design.html` contiene la especificación original: requerimientos, arquitectura, API, pipeline de procesamiento, y gestión de almacenamiento.

**Cuidado:** ese documento describe el diseño anterior, en el que el audio iba multiplexado en los segmentos de video y cambiar de idioma implicaba matar y reiniciar FFmpeg. Ese diseño se reemplazó (ver *Conceptos clave*). Siguen vigentes sus secciones de requerimientos, seguridad y stack; **no** las de API REST, selección de pistas, seek, parámetros de FFmpeg ni gestión de almacenamiento. Ante una contradicción, manda este archivo.

## Stack

- Python 3.11+ con asyncio
- FastAPI + Uvicorn
- Pydantic v2 + pydantic-settings
- FFmpeg/ffprobe invocados con `asyncio.create_subprocess_exec` (no wrappers)
- Segmentos en **fMP4 / CMAF** (`-hls_segment_type fmp4`), no MPEG-TS
- hls.js en el reproductor web (cargar desde CDN en player.html)

## Estructura del proyecto

```
stream-media/
├── app/
│   ├── main.py                 # FastAPI app, lifespan, montaje de /hls, ciclo de limpieza
│   ├── config.py               # pydantic-settings, variables de entorno
│   ├── errors.py               # ApiError (formato de error) y FFmpegError
│   ├── api/
│   │   └── routes.py           # Endpoints REST + playlists calculadas
│   ├── services/
│   │   ├── media_analyzer.py   # ffprobe, StreamStrategy, pistas, serializacion de SourceInfo
│   │   ├── transcoder.py       # Construccion de comandos FFmpeg y control del proceso
│   │   ├── playlist.py         # Logica pura: master y media playlists, strings de codec
│   │   ├── asset_store.py      # Identidad del asset, layout en disco, manifest, GC LRU
│   │   ├── asset_builder.py    # Orquesta los builds y arma las playlists
│   │   ├── session_manager.py  # Heartbeat, TTL, expiracion
│   │   └── static_server.py    # Servidor estatico del modo CLI
│   └── models/
│       └── schemas.py          # Modelos Pydantic
├── static/
│   └── player.html             # Reproductor web
├── output/                     # Cache de artefactos (persistente, ver abajo)
│   └── {asset_id}/
│       ├── manifest.json
│       ├── video/{init.mp4, seg-00000.m4s, internal.m3u8}
│       ├── audio/{n}/{init.mp4, seg-00000.m4s, internal.m3u8}
│       └── subs/sub_0_spa.vtt
├── tests/
├── transcode.py                # CLI: genera los mismos artefactos sin API
└── pyproject.toml
```

## Conceptos clave

### Assets y sesiones

Son dos cosas distintas y no hay que volver a mezclarlas:

- **Asset**: un archivo de origen y sus artefactos derivados. Identidad `(ruta, mtime, tamaño)` (`asset_store.asset_id_for`). Vive en el cache y **sobrevive entre arranques**: abrir dos veces la misma película no regenera nada.
- **Session**: un espectador. Efímera, con heartbeat y TTL. No es dueña de ningún archivo; solo protege a su asset del GC.

### Timeline absoluto

Es la invariante de la que depende todo lo demás.

El video, cada pista de audio y los subtítulos se generan en **corridas separadas de FFmpeg**. Que compartan el mismo origen de tiempo es lo único que los mantiene sincronizados. Por eso todos los comandos llevan:

```
-copyts -avoid_negative_ts disabled -muxdelay 0 -muxpreload 0
```

- **Nunca agregar `-output_ts_offset`**: con `-copyts` los PTS ya salen absolutos y sumarlo otra vez los duplica.
- **Nunca agregar `-start_at_zero`**: contradice `-copyts`.
- Los tests `test_transcoder.py::test_no_usa_output_ts_offset_ni_start_at_zero` y `::test_copyts_va_antes_del_input` existen para que esto no se rompa por accidente.

### Video y audio separados, audio como rendition group

El video se segmenta **sin audio** (`-an`) y cada pista de audio **sin video** (`-vn`). El master las declara con `#EXT-X-MEDIA:TYPE=AUDIO`.

Consecuencia: **cambiar de idioma es `hls.audioTrack = n` en el cliente**. No se recarga el manifest, no se pierde `currentTime`, no se reinicia FFmpeg, no se resetean los subtítulos y no hay corte de video. Si aparece un endpoint de "cambiar pista" o "seek", algo se hizo mal.

El audio-only corre a ~200x tiempo real, así que **todas las pistas se construyen de entrada**, en paralelo con el video. Terminan mucho antes que él.

### Dos niveles de procesamiento (solo para el video)

1. **Remux** (H.264): `-c:v copy`. Rápido.
2. **Transcodificación** (H.265 u otros): `-c:v libx264 -preset veryfast -crf 23`, más `-force_key_frames expr:gte(t,n_forced*{D})` y `-sc_threshold 0` para que los cortes de segmento caigan en múltiplos exactos.

Para el audio: `-c:a copy` si ya es AAC con la cantidad de canales de salida; si no, `-c:a aac -b:a 128k`.

La detección se hace con `ffprobe -v quiet -print_format json -show_streams -show_format`.

### Las playlists se calculan, no se sirven

FFmpeg escribe un `internal.m3u8` por pista. **Ese archivo nunca se sirve al cliente**: solo se parsea para conocer las duraciones reales de los segmentos. Las playlists que ve el navegador las arma `playlist.py` en cada request.

- Mientras el build corre, la playlist sale como **EVENT** (sin `#EXT-X-ENDLIST`): la reproducción arranca en segundos y la lista crece.
- Cuando FFmpeg cierra su playlist, pasa a **VOD** con `ENDLIST` y el archivo queda seekeable entero.
- Las duraciones `#EXTINF` son siempre las **reales** que reportó FFmpeg. Declarar `6.000` uniforme cuando los segmentos no lo son desfasa los subtítulos, y el error se acumula.
- Las media playlists de dos idiomas son idénticas salvo la URI base. Eso es lo que garantiza que el timeline no cambie al cambiar de audio.

**En `main.py`, el router se registra antes del `mount("/hls", StaticFiles(...))`.** Starlette resuelve en orden de registro: si el mount ganara, se serviría el `internal.m3u8` de FFmpeg en vez de la playlist calculada. Es un fallo silencioso; hay un test que lo cubre.

### Subtítulos

Se extraen una vez a WebVTT (`-map 0:s:{n} -c:s webvtt`) y se sirven como archivos estáticos, fuera del pipeline de audio. Sus tiempos son absolutos respecto del original, igual que los de los segmentos, así que se mantienen sincronizados en cualquier posición.

El player los carga como `<track>`. PGS y VobSub son bitmap: la extracción falla, se loguea y se sigue sin esa pista.

### Cache y limpieza

El directorio de salida es un **cache persistente**, no un temporal. No se borra al arrancar.

- **LRU con tope**: `SM_MAX_CACHE_SIZE`. Al superarlo se borran los assets menos usados que no tengan sesiones vivas ni builds en curso (`AssetStore.collect`).
- **Heartbeat + TTL**: el player manda `POST /api/v1/heartbeat/{session_id}` cada 30 s. Sin heartbeat por 2 min la sesión queda inactiva (deja de proteger su asset); a los 12 min se elimina.
- **Ciclo periódico**: `main.cleanup_loop` corre cada `SM_CLEANUP_INTERVAL`, poda sesiones y aplica el GC.
- Un asset borrado por el GC se saca también de memoria (`AssetBuilder.forget`), o se seguirían sirviendo playlists que apuntan a segmentos inexistentes.

## API

```
POST /api/v1/stream            {file_path} -> sesion + master_url + pistas
GET  /api/v1/sessions/{id}     estado y progreso del build
POST /api/v1/heartbeat/{id}    204

GET  /hls/{asset_id}/master.m3u8
GET  /hls/{asset_id}/video/playlist.m3u8
GET  /hls/{asset_id}/audio/{n}/playlist.m3u8
GET  /hls/{asset_id}/**        segmentos, init.mp4 y subtitulos (StaticFiles)
```

No hay endpoints de selección de pista ni de seek: los resuelve el cliente.

## Convenciones de código

- Español para logs, comentarios y docstrings. Inglés para nombres de variables, funciones, clases y endpoints.
- Sin acentos en comentarios y docstrings del código Python (el resto del texto sí los lleva).
- Usar `structlog` para logging estructurado (JSON).
- Async everywhere: `async def` en routes, `await` para subprocess y I/O.
- Type hints en todas las funciones públicas.
- `Path` de `pathlib` para manejo de rutas de archivo.
- No usar `shell=True` en subprocess. Siempre `create_subprocess_exec`.
- Los settings se cargan desde variables de entorno con prefijo `SM_`. No hay archivo `.env` hardcodeado.

## Manejo de errores

Respuestas de error consistentes, producidas por `ApiError` y su handler en `main.py`:

```json
{
  "error": "file_not_found",
  "detail": "El archivo no existe: /ruta/al/video.mp4"
}
```

Códigos HTTP:
- `400` — Ruta inválida, extensión no soportada, path traversal, fuera de `MEDIA_ROOT`
- `404` — Archivo no encontrado, sesión o asset inexistente, pista sin generar
- `503` — Se alcanzó `MAX_CONCURRENT_FFMPEG` y el archivo no está abierto ni cacheado
- `507` — Se alcanzó `MAX_CACHE_SIZE` y el GC no pudo liberar nada

Cuando FFmpeg falla, el artefacto queda en `failed` con su stderr guardado. Un artefacto que falla no arrastra a los demás: si falla una pista de audio, el video y los otros idiomas siguen sirviéndose.

## Seguridad de rutas

Toda ruta de entrada pasa por `routes._validate_path`:
1. Debe ser absoluta (`Path.is_absolute()`)
2. No debe contener `..` después de resolver
3. La extensión debe ser `.mp4` o `.mkv`
4. Debe existir en disco (`Path.exists()`)
5. Si `MEDIA_ROOT` está configurado, debe estar dentro de ese directorio (`Path.resolve().is_relative_to()`)

## Player (static/player.html)

- Modos de entrada: `?file=<ruta>` (abre y crea sesión), `?session=<id>` (retoma), `?src=<url>` (modo CLI, sin API).
- Carga `master_url` con hls.js; en Safari usa HLS nativo.
- Pobla el selector de audio desde `hls.audioTracks` (evento `AUDIO_TRACKS_UPDATED`) y cambia con `hls.audioTrack = n`. **Nunca llama al servidor para cambiar de idioma.**
- Selector de subtítulos con opción "Desactivados"; los carga como `<track>`.
- Barra de progreso mientras `status != "ready"`, con polling cada 2 s a `/api/v1/sessions/{id}`.
- Heartbeat cada 30 segundos.
- Diseño minimalista, fondo oscuro, video centrado, responsive.

## Testing

- `pytest` + `pytest-asyncio` + `httpx` (AsyncClient para tests de API).
- **Ningún test invoca los binarios reales.** `tests/conftest.py` provee `spawn_mock` (para `create_subprocess_exec`), `FakeFFmpeg` y el fixture `patched`, que parchea `analyze`, `extract_subtitle` y `start_ffmpeg` en `asset_builder`.
- `test_playlist.py` es el más valioso: lógica pura, sin FFmpeg ni disco. Ahí viven las invariantes de las playlists.
- `test_transcoder.py`: los flags de timestamp, la separación video/audio y la decisión de codec.
- `test_asset_store.py`: identidad del asset, manifest, LRU.
- `test_asset_builder.py`: deduplicación de builds concurrentes, cache, fallos aislados por pista.
- `test_session_manager.py`: TTL y heartbeat con reloj falso.
- `test_api.py`: endpoints, formato de error y que la ruta de playlist le gane al mount estático.

## Verificación con archivos reales

Los tests no cubren que FFmpeg haga lo que se espera. Con un `.mkv` real, después de generar los artefactos:

1. **PTS absoluto.** fMP4 necesita el init segment para decodificarse:
   concatenar `init.mp4` + `seg-00020.m4s` y correr
   `ffprobe -v error -select_streams v:0 -show_entries packet=pts_time -of csv=p=0`.
   Debe dar **~120** (20 × 6 s). Si da ~0 falta `-copyts`; si da ~240 se coló `-output_ts_offset`.
2. **Suma de `#EXTINF`** vs. `ffprobe -show_entries format=duration`, dentro de ±0.5 s.
3. **Keyframe al inicio de cada segmento**: el primer `frame=key_frame` debe ser `1`.
4. **Alineación audio/video**: el primer PTS de `audio/{n}/seg-00020.m4s` debe coincidir con el de `video/seg-00020.m4s`.
5. **Cambio de idioma**: reproducir hasta 02:00, cambiar audio → `currentTime` no se mueve, no hay corte y no aparecen procesos FFmpeg nuevos.
