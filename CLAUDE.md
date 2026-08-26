# Stream Media

Servicio auto-hospedado de streaming multimedia. Recibe una ruta de archivo de video, detecta codec, pistas de audio y subtítulos, y lo sirve al navegador vía HLS con selección de pistas de audio.

## Documento de diseño

El archivo `stream-media-design.html` contiene la especificación completa: requerimientos, arquitectura, API, pipeline de procesamiento, y gestión de almacenamiento. **Consultarlo antes de tomar decisiones de arquitectura o diseño de API.**

## Stack

- Python 3.11+ con asyncio
- FastAPI + Uvicorn
- Pydantic v2 + pydantic-settings
- FFmpeg/ffprobe invocados con `asyncio.create_subprocess_exec` (no wrappers)
- hls.js en el reproductor web (cargar desde CDN en player.html)

## Estructura del proyecto

```
stream-media/
├── app/
│   ├── __init__.py
│   ├── main.py                 # FastAPI app, lifespan (startup cleanup)
│   ├── config.py               # pydantic-settings, variables de entorno
│   ├── api/
│   │   ├── __init__.py
│   │   └── routes.py           # Endpoints REST
│   ├── services/
│   │   ├── __init__.py
│   │   ├── media_analyzer.py   # ffprobe wrapper, StreamStrategy enum, detección de pistas
│   │   ├── transcoder.py       # FFmpeg: remux, transcodificación, seek, extracción de subs
│   │   ├── job_manager.py      # Estado de jobs en memoria (dict), selección de pistas
│   │   └── session_manager.py  # Heartbeat, TTL, limpieza automática
│   └── models/
│       ├── __init__.py
│       └── schemas.py          # Modelos Pydantic (request/response, StreamStrategy, AudioTrack, SubtitleTrack)
├── static/
│   └── player.html             # Reproductor web (hls.js + selector de audio/subs + heartbeat)
├── output/                     # Directorio efímero para segmentos HLS y subtítulos
│   └── {job_id}/
│       ├── master.m3u8
│       ├── segment_000.ts
│       └── subtitles/
│           └── sub_0_spa.vtt
├── tests/
│   ├── __init__.py
│   ├── test_media_analyzer.py
│   ├── test_transcoder.py
│   ├── test_session_manager.py
│   └── test_api.py
├── requirements.txt
└── pyproject.toml
```

## Conceptos clave

### Dos niveles de procesamiento

El sistema elige automáticamente la ruta de menor costo. Toda la salida es HLS — no hay stream directo.

1. **Remux** (H.264, cualquier contenedor): Reempaqueta sin re-codificar video (`-c:v copy`). Mapea solo la pista de audio seleccionada. Rápido (~50-100x tiempo real), genera segmentos HLS temporales.
2. **Transcodificación** (H.265 u otros): Re-codifica a H.264 (`-c:v libx264 -preset veryfast -crf 23`). Mapea solo la pista de audio seleccionada. Lento, costoso en CPU.

La detección se hace con `ffprobe -v quiet -print_format json -show_streams -show_format`.

### Selección de pistas de audio y subtítulos

- Al iniciar un job, se usa la primera pista de audio y sin subtítulos.
- La respuesta del job incluye `audio_tracks` y `subtitle_tracks` con toda la metadata (idioma, codec, canales, título).
- `POST /api/v1/jobs/{id}/select` permite cambiar audio y/o subtítulos:
  - **Cambio de audio**: mata FFmpeg, reinicia con `-map 0:a:{n}` desde la posición actual. Interrumpe brevemente la reproducción.
  - **Cambio de subtítulo**: extrae la pista a WebVTT (`-map 0:s:{n} -c:s webvtt`). Es instantáneo, no interrumpe la reproducción. Se cachea en disco.
  - **Desactivar subtítulo**: enviar `subtitle_track: null`. El player remueve el `<track>`.
- Si el audio seleccionado ya es AAC, usar `-c:a copy`. Si es otro codec (AC3, DTS, FLAC, OPUS), transcodificar a AAC (`-c:a aac -b:a 128k`).

### Seek libre

El usuario puede saltar a cualquier punto de la línea de tiempo sin esperar que el video se procese secuencialmente.

- `POST /api/v1/jobs/{id}/seek` con `{ timestamp: 2700 }`.
- Mata el proceso FFmpeg actual, reinicia con `-ss {timestamp}` (antes de `-i`, input seeking).
- Usa `-start_number {timestamp/hls_time}` para mantener la numeración coherente.
- Mantiene la pista de audio seleccionada actualmente.
- Los segmentos previos se conservan en disco (disponibles si el usuario vuelve atrás).
- Los subtítulos extraídos no necesitan regenerarse (cubren todo el archivo).

### Gestión de archivos temporales

Todos los jobs generan temporales (segmentos .ts, manifest .m3u8, subtítulos .vtt). Tres mecanismos de limpieza:
- **Heartbeat + TTL**: El player envía POST cada 30s. Sin heartbeat por 2 min → inactiva. 10 min inactiva → cleanup.
- **Startup cleanup**: Al arrancar, borrar todo en `output/`.
- **Límite de almacenamiento**: Rechazar nuevos jobs si se excede `MAX_TEMP_STORAGE`.

## Convenciones de código

- Español para logs, comentarios y docstrings. Inglés para nombres de variables, funciones, clases y endpoints.
- Usar `structlog` para logging estructurado (JSON).
- Async everywhere: `async def` en routes, `await` para subprocess y I/O.
- Type hints en todas las funciones públicas.
- `Path` de `pathlib` para manejo de rutas de archivo.
- No usar `shell=True` en subprocess. Siempre `create_subprocess_exec`.
- Los settings se cargan desde variables de entorno con defaults. No hay archivo `.env` hardcodeado.

## Manejo de errores

Respuestas de error consistentes con este formato:

```json
{
  "error": "file_not_found",
  "detail": "El archivo no existe: /ruta/al/video.mp4"
}
```

Códigos HTTP:
- `400` — Ruta inválida, extensión no soportada, path traversal detectado
- `404` — Archivo no encontrado en disco, job_id no existe, pista de audio/subtítulo no existe
- `409` — Ya existe un job activo para este archivo
- `500` — FFmpeg falló (incluir stderr en el campo `detail`)
- `503` — Se alcanzó `MAX_CONCURRENT_FFMPEG`, reintentar después
- `507` — Se alcanzó `MAX_TEMP_STORAGE`, liberar espacio primero

Cuando FFmpeg falla a mitad de proceso: marcar job como `failed`, guardar stderr en el job, y limpiar los archivos temporales parciales inmediatamente.

## Seguridad de rutas

Toda ruta de entrada debe pasar por validación:
1. Debe ser absoluta (`Path.is_absolute()`)
2. No debe contener `..` después de resolver
3. Debe existir en disco (`Path.exists()`)
4. La extensión debe ser `.mp4` o `.mkv`
5. Si `MEDIA_ROOT` está configurado, debe estar dentro de ese directorio (`Path.resolve().is_relative_to()`)

## Player (static/player.html)

El reproductor debe:
- Consultar `GET /api/v1/jobs/{job_id}` para obtener info del job, pistas disponibles y selección actual
- Usar hls.js con `/stream/{id}/master.m3u8` (siempre HLS, no hay stream directo)
- Mostrar barra de progreso de procesamiento si `status != "ready"` (polling cada 2s a `/api/v1/jobs/{id}`)
- Mostrar selector de pista de audio con las opciones de `audio_tracks` (idioma y título)
- Mostrar selector de subtítulos con las opciones de `subtitle_tracks` (idioma y título), incluyendo opción "Desactivados"
- Al cambiar audio: `POST /api/v1/jobs/{id}/select { audio_track: n }`, recargar manifest HLS
- Al cambiar subtítulo: `POST /api/v1/jobs/{id}/select { subtitle_track: n }`, cargar el `.vtt` como `<track>`
- Enviar heartbeat (`POST /api/v1/heartbeat/{id}`) cada 30 segundos mientras la página esté abierta
- Al hacer seek más allá de lo procesado: `POST /api/v1/jobs/{id}/seek { timestamp: n }`, recargar manifest
- Diseño minimalista, fondo oscuro, video centrado, responsive

## Testing

- Usar `pytest` + `pytest-asyncio` + `httpx` (AsyncClient para tests de API)
- Mockear `ffprobe` y `ffmpeg` en tests unitarios (no depender de binarios)
- Tests de `media_analyzer.py`: mockear subprocess, verificar que retorna la estrategia correcta y las pistas de audio/subtítulos parseadas
- Tests de `transcoder.py`: verificar que los comandos FFmpeg se construyen correctamente con los `-map` según la pista seleccionada, con `-ss` para seek, y con `-c:s webvtt` para subtítulos
- Tests de `session_manager.py`: verificar TTL, cleanup, límite de almacenamiento
- Tests de `job_manager.py`: verificar cambio de pista de audio (kill + restart), extracción de subtítulos bajo demanda, seek (kill + restart con -ss)
- Tests de API: verificar respuestas para cada estrategia, selección de pistas, seek, y cada error

## Orden de implementación sugerido

1. `config.py` + `models/schemas.py` — fundamentos (incluir AudioTrack, SubtitleTrack, StreamStrategy)
2. `media_analyzer.py` — detección de codec y parsing de todas las pistas de audio y subtítulos
3. `transcoder.py` — remux, transcodificación, seek (kill + restart con -ss), extracción de subtítulos a WebVTT
4. `job_manager.py` — estado en memoria, selección de pistas, referencia al proceso FFmpeg activo
5. `session_manager.py` — heartbeat y cleanup
6. `routes.py` + `main.py` — conecta todo (POST /stream, POST /select, POST /seek, GET /subtitles)
7. `player.html` — frontend con selector de audio/subs y seek
8. Tests