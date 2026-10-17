# Stream Media

Servicio auto-hospedado de streaming multimedia. Recibe un archivo de video, detecta codec, pistas de audio y subtítulos, y lo sirve al navegador vía HLS con selección de pistas de audio en el cliente sin reiniciar la reproducción.

- **Audio separado del video.** Cada pista se segmenta por su cuenta en fMP4 y el master playlist las declara como rendition groups (`#EXT-X-MEDIA:TYPE=AUDIO`). Cambiar de pista es instantáneo (`hls.audioTrack = n`) en el cliente.
- **Cache persistente.** Los artefactos generados sobreviven entre arranques. Un garbage collector por LRU (`CapacityGuard` / `AssetStore`) libera espacio cuando se supera el tope configurado.
- **Reproducción temprana.** La playlist se entrega como `EVENT` mientras FFmpeg procesa y pasa a `VOD` al finalizar, permitiendo reproducir a los pocos segundos.

---

## Reglas para Agentes de Desarrollo

1. **Búsqueda de skills:** Antes de comenzar con una tarea que requiere escritura, el agente siempre debe buscar en las skills si le sirve alguna.
2. **Carga obligatoria de Ponytail:** Siempre cargar la skill `ponytail` antes de escribir en el código. Usar la skill de `ponytail` para escribir código.
3. **Git restringido a solo lectura:** El agente **no toca git salvo para lectura**. Eso lo maneja el usuario. Tiene permitido `git log`, `git status`, `git branch`, etc. Tiene prohibido agregar archivos, crear ramas, hacer merge, etc.
4. **No ejecución de herramientas externas:** El agente **no ejecuta comandos de `dotnet`, `ffmpeg`, `docker`, etc.** Siempre debe mencionar en su respuesta cómo ejecutar lo que consideró.

---

## Estado Actual del Proyecto (Backend)

El backend fue migrado de su prototipo original en Python/FastAPI a **.NET 10 (C#)** estructurado bajo **Clean Architecture**.

### Implementado
- **Catálogo y Mediateca (SQLite):**
  - Registro e inspección técnica automática vía `ffprobe` (`POST /api/v1/media`).
  - Listado paginado con búsqueda y filtros (`q`, `kind`, `in_list`, `sort`) (`GET /api/v1/media`).
  - Consulta de detalle (`GET /api/v1/media/{id}`).
  - Actualización de metadatos editoriales y pistas ignoradas (`PATCH /api/v1/media/{id}`).
- **Streaming y Procesamiento HLS:**
  - Apertura de streams y orquestación de procesos FFmpeg (`POST /api/v1/stream`).
  - Control de admisión y límites de capacidad de disco/concurrencia (`CapacityGuard`).
  - Manejo de sesiones activas y TTL con heartbeat (`SessionManager`, `POST /api/v1/heartbeat/{sessionId}`, `GET /api/v1/sessions/{sessionId}`).
  - Generación de playlists HLS al vuelo (`GetPlaylistUseCase`: master con audio renditions, video y audio playlists calculadas sin exponer `internal.m3u8`).
  - Entrega de playlists y segmentos fMP4/CMAF con soporte de rangos HTTP 206 (`HlsController`).
- **Frontend SPA/MPA:**
  - UI en React 19 + Vite servida como archivos estáticos desde `wwwroot` por ASP.NET Core (`/`, `/new/`, `/player/`).

### Pendiente / Brechas
- **Codificación asíncrona AV1:** Los endpoints `POST /api/v1/media/{id}/encode` y `GET /api/v1/media/{id}/encode` no fueron migrados aún (actualmente solo transcode a H.264 o remux en demanda).
- **Modo CLI standalone:** En el prototipo existía `transcode.py`; en .NET se priorizó la API web.

---

## Stack

- **Backend:** .NET 10 (C#), ASP.NET Core Web API, SQLite (`Microsoft.Data.Sqlite`).
- **Procesamiento multimedia:** FFmpeg y ffprobe del sistema (invocados de forma asíncrona mediante `System.Diagnostics.Process`).
- **Formato HLS:** Segmentos fMP4 / CMAF (`-hls_segment_type fmp4`), video sin audio (`-an`) y audios independientes (`-vn`).
- **Frontend:** React 19, TypeScript, Vite (MPA) y hls.js.

---

## Estructura de la Solución (`StreamMedia.slnx`)

```
src/
├── StreamMedia.Domain/          # Entidades (Media, Asset, SourceInfo), DTOs, Enums
├── StreamMedia.Application/     # Casos de uso (Media, Streaming), AssetBuilder, PlaylistBuilder
├── StreamMedia.Infrastructure/  # SQLite (Dapper/ADONET), AssetStore en disco, runners FFmpeg/ffprobe
└── StreamMedia.WebApi/          # Controllers, Middlewares de estáticos/HLS, Program.cs
tests/
├── StreamMedia.Domain.Tests/
├── StreamMedia.Application.Tests/
├── StreamMedia.Infrastructure.Tests/
└── StreamMedia.WebApi.Tests/
```

---

## Puesta en marcha

### Con Docker Compose (vía recomendada)

```bash
cp .env.example .env     # Configurar MEDIA_DIR con la ruta de videos en el host
docker compose up --build
```

Queda publicado en `http://localhost:8000`.

### Desarrollo local

- **Backend (.NET 10):**
  ```bash
  dotnet build StreamMedia.slnx
  dotnet test StreamMedia.slnx
  dotnet run --project src/StreamMedia.WebApi
  ```

- **Frontend (Vite):**
  ```bash
  cd frontend
  npm install
  npm run dev            # http://localhost:5173 (con proxy hacia la API)
  ```

---

## Configuración

Se configura vía variables de entorno o `appsettings.json`:

| Variable Compose / Env | Config .NET (`appsettings`) | Default | Descripción |
|---|---|---|---|
| `MEDIA_DIR` | — | — | Directorio del host montado en `/media` |
| `SM_MEDIA_ROOT` | `Media:Root` | `/media` | Raíz donde residen los videos fuente |
| `SM_CACHE_DIR` | `Cache:Dir` | `/output` | Directorio donde residen los artefactos HLS |
| `SM_CACHE_MAX_BYTES` | `Cache:MaxBytes` | `53687091200` (50 GiB) | Límite máximo de cache para el recolector LRU |
| `Database__Path` | `Database:Path` | `/data/media.sqlite` | Ruta de la base de datos SQLite |
| `SM_MAX_CONCURRENT_FFMPEG` | `Ffmpeg:MaxConcurrent` | `2` | Máximo de procesos FFmpeg simultáneos |
| `PORT` | — | `8000` | Puerto expuesto en el host |

---

## API

```
# Catálogo
POST   /api/v1/media                     Registrar archivo de video ({ file_path })
GET    /api/v1/media                     Listado paginado (?q=, ?kind=, ?in_list=, ?sort=)
GET    /api/v1/media/{id}                Detalle de una película
PATCH  /api/v1/media/{id}                Actualizar metadatos y pistas ignoradas

# Streaming & Sesiones
POST   /api/v1/stream                    Iniciar stream ({ id_media } o { file_path })
GET    /api/v1/sessions/{sessionId}      Estado del build y reproducibilidad
POST   /api/v1/heartbeat/{sessionId}     Renovar TTL de la sesión

# Playlists y Segmentos HLS
GET    /hls/{assetId}/master.m3u8        Master playlist (renditions de audio)
GET    /hls/{assetId}/video/playlist.m3u8 Playlist de video (EVENT/VOD)
GET    /hls/{assetId}/audio/{track}/playlist.m3u8 Playlist de audio (EVENT/VOD)
GET    /hls/{assetId}/video/{file}       Init o segmento de video (.mp4, .m4s)
GET    /hls/{assetId}/audio/{track}/{file} Init o segmento de audio (.mp4, .m4s)
GET    /hls/{assetId}/subs/{file}        Subtítulos WebVTT (.vtt)
```

---

## Licencia

MIT — ver [LICENSE](LICENSE).
