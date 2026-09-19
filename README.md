# Stream Media

Servicio auto-hospedado de streaming multimedia. Recibe un archivo de video,
detecta codec, pistas de audio y subtítulos, y lo sirve al navegador vía HLS
con selección de pistas de audio en el cliente.

- **Audio separado del video.** Cada pista se segmenta por su cuenta y el
  master las declara como rendition group, así que cambiar de idioma es
  `hls.audioTrack = n`: no se recarga el manifest, no se pierde `currentTime`
  y no se reinicia FFmpeg.
- **Cache persistente.** Los artefactos sobreviven entre arranques: abrir dos
  veces la misma película no regenera nada. Un GC por LRU libera lo menos
  usado cuando se supera el tope.
- **Reproducción temprana.** La playlist sale como EVENT mientras el build
  corre y pasa a VOD al terminar, así que se puede mirar a los pocos segundos.
- **Codificación de biblioteca.** Además del remux rápido para mirar ahora,
  hay un pipeline asíncrono que codifica a AV1 (SVT-AV1) para archivar.

## Stack

Python 3.11+ con asyncio · FastAPI + Uvicorn · Pydantic v2 · FFmpeg/ffprobe
como binarios del sistema · segmentos fMP4/CMAF · React + Vite y hls.js en el
frontend.

## Puesta en marcha

Con Docker Compose, que es el camino soportado:

```bash
cp .env.example .env     # ajustar MEDIA_DIR al directorio con los videos
docker compose up --build
```

Queda en `http://localhost:8000`. El directorio de videos se monta read-only
en `/media` y el cache de artefactos en `./output`.

El flujo es de tres pasos: dar de alta la película (`/new/`), elegir en la
ficha qué pistas vale la pena generar, y recién ahí reproducir. El alta no
codifica nada, así que entre un paso y el otro no hay trabajo que cancelar.

### Vistas

| Ruta            | Qué es                                      |
|-----------------|---------------------------------------------|
| `/`             | Galería del catálogo                        |
| `/new/`         | Alta de una película                        |
| `/player/`      | Reproductor                                 |
| `/docs`         | OpenAPI de la API                           |
| `/static/player.html` | Player vanilla del MVP, sin enlaces   |

## Configuración

Todo sale de variables de entorno con prefijo `SM_` (ver `app/config.py`).
Las que importan:

| Variable                  | Default              | Qué hace                                         |
|---------------------------|----------------------|--------------------------------------------------|
| `SM_MEDIA_ROOT`           | —                    | Raíz de los videos. Sin esto el catálogo no anda |
| `SM_CACHE_DIR`            | `output`             | Dónde viven los artefactos                        |
| `SM_DB_PATH`              | `data/media.sqlite`  | Catálogo. Fuera del cache, que tiene GC por tamaño |
| `SM_MAX_CACHE_SIZE`       | 50 GiB               | Tope del cache antes de que entre el LRU          |
| `SM_MAX_CONCURRENT_FFMPEG`| `3`                  | Procesos FFmpeg simultáneos                       |
| `SM_HLS_TIME`             | `6`                  | Duración de segmento en segundos                  |
| `SM_VIDEO_CODEC`          | `h264`               | Encoder de salida (`app/codecs`)                  |

El `.env` de Compose usa nombres propios más cortos (`MEDIA_DIR`, `PORT`,
`DEBUG`); están documentados en `.env.example`.

## API

```
POST  /api/v1/media                 {file_path relativo} -> ficha + pistas detectadas
GET   /api/v1/media                 listado paginado
PATCH /api/v1/media/{id}            editoriales + qué pistas no generar
POST  /api/v1/media/{id}/encode     arranca la codificación AV1
GET   /api/v1/media/{id}/encode     estado de esa codificación

POST  /api/v1/stream                {id_media | file_path} -> sesión + master_url
GET   /api/v1/sessions/{id}         estado y progreso del build
POST  /api/v1/heartbeat/{id}        mantiene viva la sesión

GET   /hls/{asset_id}/master.m3u8   y las playlists y segmentos derivados
```

No hay endpoint para cambiar de pista de audio ni para hacer seek: las dos
cosas las resuelve el cliente sobre el mismo timeline.

## Desarrollo

```bash
pip install -e ".[dev]"
pytest
```

Ningún test invoca los binarios reales: FFmpeg y ffprobe están mockeados.

Para el frontend con hot reload, contra la API del contenedor:

```bash
docker compose up frontend      # http://localhost:5173
```

### Modo CLI

Genera los mismos artefactos sin pasar por la API:

```bash
docker compose run --rm stream-media \
  python -m app.services.transcode /media/peli.mkv --serve
```

### Verificación con archivos reales

Los tests no cubren que FFmpeg haga lo que se espera.
`scripts/verify_timeline.py` corre esas comprobaciones sobre artefactos ya
generados — que los PTS sean absolutos, que los `#EXTINF` sumen la duración
del original y que audio y video queden alineados:

```bash
python scripts/verify_timeline.py output/{asset_id}
```

Necesita `ffprobe` en el PATH.

## Licencia

MIT — ver [LICENSE](LICENSE).

Dos aclaraciones sobre lo que el proyecto usa pero no distribuye:

- **FFmpeg.** Se invoca como proceso separado, nunca linkeado, así que su
  licencia no alcanza a este código. La imagen Docker sí incluye el paquete
  `ffmpeg` de Debian, que va con libx264 y es GPLv2+: si alguna vez se publica
  la imagen ya construida en un registry, esa redistribución se rige por la
  GPL y corresponde apuntar a las fuentes de Debian.
- **Patentes de codecs.** H.264 y HEVC tienen pools de patentes (MPEG LA,
  Access Advance) y AV1 se rige por la licencia de patentes de AOMedia. La
  licencia MIT cubre el código, no otorga derechos de patente sobre los codecs
  y no cambia nada de eso.
