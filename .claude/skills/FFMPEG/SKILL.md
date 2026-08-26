# FFmpeg en Python — Skill para casos simples

Guía para usar FFmpeg desde Python en operaciones comunes de procesamiento de video y audio. Cubre tres enfoques: la librería **ffmpeg-python** (kkroening), la librería **python-ffmpeg** (con soporte async nativo), y **asyncio.create_subprocess_exec** (invocación directa). Enfocada en casos simples y recetas prácticas.

**Usar esta skill cuando:** se necesite procesar video o audio desde Python — convertir formatos, extraer audio, recortar, redimensionar, generar thumbnails, concatenar, agregar watermarks, analizar archivos con ffprobe, o generar segmentos HLS. Para casos simples y recetas directas.

---

## Decisión de enfoque

Antes de escribir código, elegir el enfoque según el contexto del proyecto:

| Enfoque | Cuándo usarlo | Instalación |
|---|---|---|
| **`ffmpeg-python`** (kkroening) | Scripts simples, prototipos, pipelines síncronos. API fluida y bien documentada | `pip install ffmpeg-python` |
| **`python-ffmpeg`** (jonghwanhyeon) | Proyectos async (FastAPI, aiohttp). Tiene API async nativa y eventos de progreso | `pip install python-ffmpeg` |
| **`asyncio.subprocess`** | Control total, proyectos donde no se quiere dependencia extra. Ideal para servidores async con manejo de procesos (kill, seek, restart) | Solo stdlib |

**Regla general:** para scripts y tareas puntuales, usar `ffmpeg-python`. Para servidores async que necesitan controlar el proceso FFmpeg (detenerlo, reiniciarlo, leer progreso), usar `asyncio.create_subprocess_exec` directamente.

IMPORTANTE: los tres enfoques requieren que FFmpeg esté instalado en el sistema y accesible en el `PATH`. Ninguna librería Python incluye FFmpeg — son wrappers.

```bash
# Verificar que FFmpeg está disponible
ffmpeg -version
ffprobe -version
```

---

## 1. ffmpeg-python (kkroening)

### 1.1 Instalación

```bash
pip install ffmpeg-python
```

CUIDADO: se instala como `ffmpeg-python` pero se importa como `ffmpeg`:

```python
import ffmpeg  # NO "import ffmpeg-python"
```

### 1.2 Modelo mental

La librería construye un grafo de operaciones que se compila a un comando FFmpeg y se ejecuta al final. Todo es lazy hasta llamar `.run()`.

```python
# Esto NO ejecuta nada todavía — solo construye el grafo
stream = ffmpeg.input('input.mp4').filter('scale', 1280, 720).output('output.mp4')

# Esto ejecuta FFmpeg
stream.run()

# Para ver qué comando se ejecutaría (debug):
print(ffmpeg.compile(stream))
# → ['ffmpeg', '-i', 'input.mp4', '-vf', 'scale=1280:720', 'output.mp4']
```

### 1.3 Conversión de formato

```python
import ffmpeg

# Básico — FFmpeg elige los codecs según la extensión de salida
ffmpeg.input('input.mp4').output('output.webm').run()

# Con codec explícito
ffmpeg.input('input.mp4').output('output.webm', vcodec='libvpx-vp9').run()

# MKV a MP4 (remux, sin re-codificar)
ffmpeg.input('input.mkv').output('output.mp4', c='copy').run()

# Con overwrite automático (equivale a -y)
ffmpeg.input('input.mp4').output('output.avi').overwrite_output().run()
```

### 1.4 Extraer audio

```python
import ffmpeg

# Extraer audio a MP3
ffmpeg.input('input.mp4').output('audio.mp3').run()

# Con codec y bitrate explícitos
ffmpeg.input('input.mp4').output('audio.mp3', acodec='libmp3lame', audio_bitrate='192k').run()

# Extraer audio sin re-codificar (si ya es AAC → .m4a)
ffmpeg.input('input.mp4').output('audio.m4a', c='copy', map='0:a:0').run()
```

### 1.5 Recortar video (trim)

```python
import ffmpeg

# Por timestamps (formato HH:MM:SS)
ffmpeg.input('input.mp4', ss='00:01:30', to='00:02:45').output('clip.mp4').run()

# Por segundos
ffmpeg.input('input.mp4', ss=90, t=75).output('clip.mp4').run()

# Con re-codificación para corte preciso
ffmpeg.input('input.mp4', ss=90, t=75).output('clip.mp4', vcodec='libx264', acodec='aac').run()

# Sin re-codificar (corte en keyframe más cercano, rápido pero impreciso)
ffmpeg.input('input.mp4', ss=90, t=75).output('clip.mp4', c='copy').run()
```

Notas sobre seeking:
- `ss` antes del input (como kwarg de `input()`) = input seeking, rápido, salta al keyframe cercano.
- `ss` como kwarg de `output()` = output seeking, preciso pero lento (decodifica todo hasta el punto).
- Para cortes precisos sin re-codificar, la combinación rápida es `ss` en input + corta duración con `t` en output.

### 1.6 Redimensionar / escalar

```python
import ffmpeg

# Escalar a resolución fija
ffmpeg.input('input.mp4').filter('scale', 1280, 720).output('output.mp4').run()

# Mantener aspect ratio (ancho fijo, alto auto)
ffmpeg.input('input.mp4').filter('scale', 1280, -1).output('output.mp4').run()

# Mantener aspect ratio (alto fijo, ancho auto — divisible por 2)
ffmpeg.input('input.mp4').filter('scale', -2, 720).output('output.mp4').run()
```

IMPORTANTE: usar `-2` en lugar de `-1` cuando se necesita que la dimensión sea divisible por 2 (requerido por la mayoría de los codecs como H.264).

### 1.7 Generar thumbnails

```python
import ffmpeg

# Un frame en un timestamp específico
ffmpeg.input('input.mp4', ss='00:00:05').output('thumb.png', vframes=1).run()

# Varios frames (uno por segundo)
ffmpeg.input('input.mp4').output('frame_%04d.png', vf='fps=1').run()

# Thumbnail automático (FFmpeg analiza y elige el mejor frame)
ffmpeg.input('input.mp4').filter('thumbnail', n=300).output('thumb.png', vframes=1).run()

# Thumbnail como grid de escenas
ffmpeg.input('input.mp4').filter('fps', fps='1/60').filter('scale', 320, -1).output('scene_%03d.png').run()
```

### 1.8 Concatenar videos

```python
import ffmpeg

# Concatenar dos videos (misma resolución, codecs y framerate)
v1 = ffmpeg.input('video1.mp4')
v2 = ffmpeg.input('video2.mp4')

ffmpeg.concat(v1, v2).output('combined.mp4').run()

# Concatenar con audio
v1 = ffmpeg.input('video1.mp4')
v2 = ffmpeg.input('video2.mp4')

ffmpeg.concat(v1.video, v1.audio, v2.video, v2.audio, v=1, a=1).output('combined.mp4').run()
```

IMPORTANTE: para `concat`, todos los videos deben tener la misma resolución, framerate, y codecs. Si no coinciden, escalarlos primero con `.filter('scale', ...)`.

### 1.9 Agregar watermark

```python
import ffmpeg

video = ffmpeg.input('input.mp4')
logo = ffmpeg.input('logo.png')

# Esquina superior izquierda
ffmpeg.overlay(video, logo, x=10, y=10).output(video.audio, 'output.mp4').run()

# Esquina inferior derecha
ffmpeg.overlay(video, logo, x='main_w-overlay_w-10', y='main_h-overlay_h-10').output(video.audio, 'output.mp4').run()

# Centro
ffmpeg.overlay(video, logo, x='(main_w-overlay_w)/2', y='(main_h-overlay_h)/2').output(video.audio, 'output.mp4').run()
```

IMPORTANTE: al usar `overlay`, el audio se pierde del stream de video. Agregar `video.audio` como primer argumento de `.output()` para preservarlo.

### 1.10 Procesar audio y video por separado

```python
import ffmpeg

input_file = ffmpeg.input('input.mp4')

# Separar streams
video = input_file.video.hflip()                                    # Flip horizontal
audio = input_file.audio.filter('aecho', 0.8, 0.9, 1000, 0.3)     # Eco

# Recombinar
ffmpeg.output(video, audio, 'output.mp4').run()
```

### 1.11 Analizar archivo con ffprobe

```python
import ffmpeg

probe = ffmpeg.probe('input.mp4')

# Información general del formato
format_info = probe['format']
print(f"Duración: {float(format_info['duration']):.1f}s")
print(f"Tamaño: {int(format_info['size']) / 1024 / 1024:.1f} MB")
print(f"Bitrate: {int(format_info['bit_rate']) / 1000:.0f} kbps")

# Buscar stream de video
video_stream = next(
    (s for s in probe['streams'] if s['codec_type'] == 'video'),
    None
)

if video_stream:
    print(f"Codec: {video_stream['codec_name']}")
    print(f"Resolución: {video_stream['width']}x{video_stream['height']}")
    print(f"FPS: {video_stream['r_frame_rate']}")

# Buscar streams de audio
audio_streams = [s for s in probe['streams'] if s['codec_type'] == 'audio']
for i, a in enumerate(audio_streams):
    lang = a.get('tags', {}).get('language', '???')
    print(f"Audio {i}: {a['codec_name']}, {a.get('channels', '?')}ch, idioma={lang}")

# Buscar streams de subtítulos
sub_streams = [s for s in probe['streams'] if s['codec_type'] == 'subtitle']
for i, s in enumerate(sub_streams):
    lang = s.get('tags', {}).get('language', '???')
    print(f"Subtítulo {i}: {s['codec_name']}, idioma={lang}")
```

### 1.12 Manejo de errores

```python
import ffmpeg
import sys

try:
    ffmpeg.input('input.mp4').output('output.mp4').overwrite_output().run(capture_stderr=True)
except ffmpeg.Error as e:
    print('FFmpeg error:', e.stderr.decode('utf-8'), file=sys.stderr)
    raise
```

SIEMPRE usar `capture_stderr=True` en `.run()` cuando se quiera capturar errores. Sin esto, `e.stderr` es `None`.

### 1.13 Ejecución async

```python
import ffmpeg

# run_async devuelve un subprocess.Popen
process = (
    ffmpeg
    .input('input.mp4')
    .output('output.mp4')
    .overwrite_output()
    .run_async(pipe_stderr=True)
)

# Esperar a que termine
stdout, stderr = process.communicate()

if process.returncode != 0:
    print(f'Error: {stderr.decode()}')
```

### 1.14 Debug — ver el comando generado

```python
import ffmpeg

stream = ffmpeg.input('in.mp4').filter('scale', 1280, 720).output('out.mp4', vcodec='libx264', crf=23)

# Ver el comando completo
print(' '.join(ffmpeg.compile(stream)))
# → ffmpeg -i in.mp4 -vf scale=1280:720 -vcodec libx264 -crf 23 out.mp4

# Solo los argumentos (sin 'ffmpeg')
print(ffmpeg.get_args(stream))
```

---

## 2. python-ffmpeg (jonghwanhyeon) — API async nativa

### 2.1 Instalación

```bash
pip install python-ffmpeg
```

CUIDADO: se importa como `ffmpeg` pero el paquete pip es `python-ffmpeg` (distinto de `ffmpeg-python`). No instalar ambos al mismo tiempo.

### 2.2 Uso síncrono

```python
from ffmpeg import FFmpeg

ffmpeg = (
    FFmpeg()
    .option('y')                         # Overwrite
    .input('input.mp4')
    .output(
        'output.mp4',
        {'codec:v': 'libx264'},          # Opciones con : se pasan como dict
        vf='scale=1280:-1',
        preset='veryfast',
        crf=23,
    )
)

ffmpeg.execute()
```

### 2.3 Uso asíncrono (FastAPI, aiohttp, etc.)

```python
import asyncio
from ffmpeg.asyncio import FFmpeg

async def transcode(input_path: str, output_path: str):
    ffmpeg = (
        FFmpeg()
        .option('y')
        .input(input_path)
        .output(
            output_path,
            {'codec:v': 'libx264'},
            preset='veryfast',
            crf=23,
        )
    )
    await ffmpeg.execute()

asyncio.run(transcode('input.mp4', 'output.mp4'))
```

### 2.4 Monitoreo de progreso

```python
from ffmpeg import FFmpeg, Progress

ffmpeg = (
    FFmpeg()
    .option('y')
    .input('input.mp4')
    .output('output.mp4', {'codec:v': 'libx264'})
)

@ffmpeg.on('progress')
def on_progress(progress: Progress):
    print(f'Frame: {progress.frame}, FPS: {progress.fps}, Time: {progress.time}')

@ffmpeg.on('completed')
def on_completed():
    print('¡Completado!')

@ffmpeg.on('error')
def on_error(code):
    print(f'Error, código de salida: {code}')

ffmpeg.execute()
```

### 2.5 Cancelar proceso

```python
from ffmpeg import FFmpeg, Progress

ffmpeg = FFmpeg().option('y').input('input.mp4').output('output.mp4')

@ffmpeg.on('progress')
def check_progress(progress: Progress):
    if progress.frame > 500:
        ffmpeg.terminate()  # Detiene el proceso FFmpeg

ffmpeg.execute()
```

---

## 3. asyncio.create_subprocess_exec (invocación directa)

Este enfoque es el recomendado cuando se necesita control total sobre el proceso FFmpeg: detenerlo para seek, reiniciarlo con otros argumentos, parsear progreso de stderr en tiempo real, o correr dentro de un servidor async como FastAPI.

### 3.1 Ejecución básica

```python
import asyncio

async def run_ffmpeg(*args: str) -> tuple[bytes, bytes]:
    """Ejecuta FFmpeg con los argumentos dados. Lanza RuntimeError si falla."""
    process = await asyncio.create_subprocess_exec(
        'ffmpeg', *args,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
    )
    stdout, stderr = await process.communicate()

    if process.returncode != 0:
        raise RuntimeError(f'FFmpeg falló (código {process.returncode}):\n{stderr.decode()}')

    return stdout, stderr

# Uso
async def main():
    await run_ffmpeg(
        '-i', 'input.mp4',
        '-c:v', 'libx264',
        '-crf', '23',
        '-preset', 'veryfast',
        '-y',
        'output.mp4',
    )

asyncio.run(main())
```

### 3.2 ffprobe async

```python
import asyncio
import json

async def ffprobe(file_path: str) -> dict:
    """Ejecuta ffprobe y retorna el resultado como dict."""
    process = await asyncio.create_subprocess_exec(
        'ffprobe',
        '-v', 'quiet',
        '-print_format', 'json',
        '-show_streams',
        '-show_format',
        file_path,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
    )
    stdout, stderr = await process.communicate()

    if process.returncode != 0:
        raise RuntimeError(f'ffprobe falló: {stderr.decode()}')

    return json.loads(stdout.decode())

# Uso
async def main():
    info = await ffprobe('input.mp4')

    video = next((s for s in info['streams'] if s['codec_type'] == 'video'), None)
    if video:
        print(f"{video['codec_name']} {video['width']}x{video['height']}")

    duration = float(info['format']['duration'])
    print(f"Duración: {duration:.1f}s")

asyncio.run(main())
```

### 3.3 Proceso controlable (kill, restart)

Este patrón es ideal para servidores de streaming donde se necesita detener FFmpeg al cambiar de pista de audio o hacer seek:

```python
import asyncio

class FFmpegProcess:
    """Wrapper para un proceso FFmpeg que se puede detener y reiniciar."""

    def __init__(self):
        self.process: asyncio.subprocess.Process | None = None

    async def start(self, args: list[str]) -> asyncio.subprocess.Process:
        """Inicia FFmpeg con los argumentos dados."""
        await self.stop()  # Detener proceso anterior si existe

        self.process = await asyncio.create_subprocess_exec(
            'ffmpeg', *args,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
        )
        return self.process

    async def stop(self):
        """Detiene el proceso FFmpeg actual si está corriendo."""
        if self.process and self.process.returncode is None:
            self.process.terminate()
            try:
                await asyncio.wait_for(self.process.wait(), timeout=5.0)
            except asyncio.TimeoutError:
                self.process.kill()
                await self.process.wait()
        self.process = None

    @property
    def is_running(self) -> bool:
        return self.process is not None and self.process.returncode is None

# Uso para streaming con seek
async def main():
    ffmpeg = FFmpegProcess()

    # Iniciar procesamiento
    await ffmpeg.start([
        '-i', 'input.mp4',
        '-map', '0:v:0', '-map', '0:a:0',
        '-c:v', 'copy', '-c:a', 'aac', '-b:a', '128k',
        '-f', 'hls', '-hls_time', '6', '-hls_list_size', '0',
        '-hls_segment_filename', 'output/segment_%03d.ts',
        '-y', 'output/master.m3u8',
    ])

    # Simular seek: detener y reiniciar desde otro punto
    await asyncio.sleep(5)
    await ffmpeg.start([
        '-ss', '2700',
        '-i', 'input.mp4',
        '-map', '0:v:0', '-map', '0:a:0',
        '-c:v', 'copy', '-c:a', 'aac', '-b:a', '128k',
        '-f', 'hls', '-hls_time', '6', '-hls_list_size', '0',
        '-start_number', '450',
        '-hls_segment_filename', 'output/segment_%03d.ts',
        '-y', 'output/master.m3u8',
    ])

    # Esperar a que termine
    if ffmpeg.process:
        await ffmpeg.process.wait()

asyncio.run(main())
```

### 3.4 Parsear progreso de stderr

FFmpeg reporta progreso por stderr. Este patrón lo lee línea por línea sin bloquear:

```python
import asyncio
import re

async def run_ffmpeg_with_progress(args: list[str], duration: float):
    """Ejecuta FFmpeg y reporta progreso como porcentaje."""
    process = await asyncio.create_subprocess_exec(
        'ffmpeg', *args,
        stdout=asyncio.subprocess.DEVNULL,
        stderr=asyncio.subprocess.PIPE,
    )

    time_pattern = re.compile(r'time=(\d+):(\d+):(\d+)\.(\d+)')

    while True:
        line = await process.stderr.readline()
        if not line:
            break

        text = line.decode('utf-8', errors='replace')
        match = time_pattern.search(text)
        if match:
            h, m, s, cs = match.groups()
            current = int(h) * 3600 + int(m) * 60 + int(s) + int(cs) / 100
            progress = min(current / duration * 100, 100.0)
            print(f'\rProgreso: {progress:.1f}%', end='', flush=True)

    await process.wait()
    print()  # Nueva línea final

    if process.returncode != 0:
        raise RuntimeError(f'FFmpeg falló con código {process.returncode}')

# Uso
async def main():
    # Primero obtener la duración
    info = await ffprobe('input.mp4')  # Función de la sección 3.2
    duration = float(info['format']['duration'])

    await run_ffmpeg_with_progress(
        ['-i', 'input.mp4', '-c:v', 'libx264', '-crf', '23', '-preset', 'veryfast', '-y', 'output.mp4'],
        duration,
    )

asyncio.run(main())
```

---

## 4. Recetas rápidas

### 4.1 Convertir formato (cualquier enfoque)

```python
# ffmpeg-python
ffmpeg.input('in.mkv').output('out.mp4').overwrite_output().run()

# python-ffmpeg
FFmpeg().option('y').input('in.mkv').output('out.mp4').execute()

# asyncio subprocess
await run_ffmpeg('-i', 'in.mkv', '-y', 'out.mp4')
```

### 4.2 Extraer audio a MP3

```python
# ffmpeg-python
ffmpeg.input('in.mp4').output('audio.mp3', acodec='libmp3lame', audio_bitrate='192k').overwrite_output().run()

# asyncio subprocess
await run_ffmpeg('-i', 'in.mp4', '-vn', '-acodec', 'libmp3lame', '-b:a', '192k', '-y', 'audio.mp3')
```

### 4.3 Recortar video (de 1:30 a 2:45)

```python
# ffmpeg-python
ffmpeg.input('in.mp4', ss='00:01:30', to='00:02:45').output('clip.mp4', c='copy').overwrite_output().run()

# asyncio subprocess
await run_ffmpeg('-ss', '00:01:30', '-to', '00:02:45', '-i', 'in.mp4', '-c', 'copy', '-y', 'clip.mp4')
```

### 4.4 Thumbnail en un punto específico

```python
# ffmpeg-python
ffmpeg.input('in.mp4', ss='00:00:10').output('thumb.jpg', vframes=1).overwrite_output().run()

# asyncio subprocess
await run_ffmpeg('-ss', '00:00:10', '-i', 'in.mp4', '-vframes', '1', '-y', 'thumb.jpg')
```

### 4.5 Escalar a 720p

```python
# ffmpeg-python
ffmpeg.input('in.mp4').filter('scale', -2, 720).output('out.mp4').overwrite_output().run()

# asyncio subprocess
await run_ffmpeg('-i', 'in.mp4', '-vf', 'scale=-2:720', '-y', 'out.mp4')
```

### 4.6 Remux MKV a MP4 (sin re-codificar)

```python
# ffmpeg-python
ffmpeg.input('in.mkv').output('out.mp4', c='copy').overwrite_output().run()

# asyncio subprocess
await run_ffmpeg('-i', 'in.mkv', '-c', 'copy', '-y', 'out.mp4')
```

### 4.7 Transcodificar H.265 a H.264

```python
# ffmpeg-python
ffmpeg.input('in.mkv').output(
    'out.mp4',
    vcodec='libx264',
    acodec='aac',
    crf=23,
    preset='veryfast',
    audio_bitrate='128k',
).overwrite_output().run()

# asyncio subprocess
await run_ffmpeg(
    '-i', 'in.mkv',
    '-c:v', 'libx264', '-crf', '23', '-preset', 'veryfast',
    '-c:a', 'aac', '-b:a', '128k',
    '-y', 'out.mp4',
)
```

### 4.8 Extraer subtítulos a WebVTT

```python
# ffmpeg-python
ffmpeg.input('in.mkv').output('sub.vtt', map='0:s:0', c='webvtt').overwrite_output().run()

# asyncio subprocess
await run_ffmpeg('-i', 'in.mkv', '-map', '0:s:0', '-c:s', 'webvtt', '-y', 'sub.vtt')
```

### 4.9 Generar HLS

```python
# ffmpeg-python
ffmpeg.input('in.mp4').output(
    'output/master.m3u8',
    format='hls',
    hls_time=6,
    hls_list_size=0,
    hls_segment_filename='output/segment_%03d.ts',
    vcodec='copy',
    acodec='aac',
    audio_bitrate='128k',
).overwrite_output().run()

# asyncio subprocess
await run_ffmpeg(
    '-i', 'in.mp4',
    '-c:v', 'copy', '-c:a', 'aac', '-b:a', '128k',
    '-f', 'hls', '-hls_time', '6', '-hls_list_size', '0',
    '-hls_segment_filename', 'output/segment_%03d.ts',
    '-y', 'output/master.m3u8',
)
```

### 4.10 Crear GIF

```python
# ffmpeg-python
ffmpeg.input('in.mp4', ss=5, t=3).filter('fps', fps=10).filter('scale', 320, -1).output('out.gif').overwrite_output().run()

# asyncio subprocess
await run_ffmpeg('-ss', '5', '-t', '3', '-i', 'in.mp4', '-vf', 'fps=10,scale=320:-1', '-y', 'out.gif')
```

---

## 5. Referencia rápida de opciones FFmpeg

Estas son las opciones más usadas, como se pasan en cada enfoque:

| FFmpeg CLI | ffmpeg-python (output kwarg) | Descripción |
|---|---|---|
| `-c:v libx264` | `vcodec='libx264'` | Codec de video |
| `-c:a aac` | `acodec='aac'` | Codec de audio |
| `-c copy` | `c='copy'` | Copiar sin re-codificar |
| `-b:v 2M` | `video_bitrate='2M'` | Bitrate de video |
| `-b:a 128k` | `audio_bitrate='128k'` | Bitrate de audio |
| `-crf 23` | `crf=23` | Calidad constante (0-51) |
| `-preset veryfast` | `preset='veryfast'` | Velocidad de encoding |
| `-f hls` | `format='hls'` | Formato de salida |
| `-vf scale=1280:-1` | `.filter('scale', 1280, -1)` | Filtro de video |
| `-ss 90` | `ss=90` (en input) | Seek al segundo 90 |
| `-t 30` | `t=30` | Duración de 30 segundos |
| `-to 00:02:00` | `to='00:02:00'` | Hasta el minuto 2 |
| `-map 0:v:0` | `map='0:v:0'` | Seleccionar stream |
| `-vframes 1` | `vframes=1` | Número de frames de video |
| `-vn` | `vn=None` | Sin video |
| `-an` | `an=None` | Sin audio |
| `-y` | `.overwrite_output()` | Sobreescribir salida |
| `-hls_time 6` | `hls_time=6` | Duración de segmento HLS |
| `-hls_list_size 0` | `hls_list_size=0` | Todos los segmentos en playlist |
| `-start_number N` | `start_number=N` | Número inicial de segmentos |

### Presets de encoding (libx264)

De más rápido a más lento (mejor compresión):
`ultrafast` → `superfast` → `veryfast` → `faster` → `fast` → `medium` → `slow` → `slower` → `veryslow`

**Regla general:** usar `veryfast` para streaming/procesamiento en tiempo real, `medium` (default) para archivos finales, `slow` cuando la calidad importa más que el tiempo.

### CRF (Constant Rate Factor) para libx264

- `0` = lossless
- `18` = visualmente lossless
- `23` = default, buen balance
- `28` = bueno para streaming
- `51` = peor calidad posible

**Regla general:** entre 18 y 28 para la mayoría de los casos. Cada +6 aproximadamente duplica el tamaño del archivo.

---

## 6. Errores comunes

**"No such file or directory":**
Verificar que `ffmpeg` y `ffprobe` están en el PATH. En Docker/containers, instalar con `apt-get install -y ffmpeg`.

**"Output file already exists":**
Agregar `.overwrite_output()` (ffmpeg-python) o `-y` (CLI/subprocess).

**"Invalid data found when processing input":**
El archivo está corrupto o el formato no es soportado. Verificar con `ffprobe`.

**"Unknown encoder":**
El codec no está compilado en la versión de FFmpeg. Verificar con `ffmpeg -encoders | grep nombre`.

**"width not divisible by 2":**
Al escalar, usar `-2` en lugar de `-1` para asegurar divisibilidad: `scale=-2:720`.

**Proceso FFmpeg queda colgado:**
Casi siempre es porque FFmpeg espera input interactivo (e.g. confirmación de overwrite). Usar `-y` siempre en scripts.

**`ffmpeg.Error` con stderr=None:**
Se olvidó pasar `capture_stderr=True` a `.run()`. Sin eso, el stderr se imprime directo a la consola y no se captura en la excepción.