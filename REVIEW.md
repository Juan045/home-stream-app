# REVIEW: Invariantes, deuda de rendimiento y cobertura de tests

Lo que hay que revisar periódicamente contra el código. La arquitectura está en
`system_arquitecture.md`; los bugs y decisiones pendientes, en `TODO.md`. Cada punto referenciado desde otro documento lleva debajo del título una línea `> Referenciado en:` con `archivo:línea`; al renumerar, resolver o mover puntos, corregir esas referencias y las líneas citadas.

---

## 1. Invariantes de arquitectura

Las restricciones que el código da por ciertas. Romper cualquiera de estas no
produce un error: produce un bug silencioso.

### I1 · Timeline absoluto

Video y cada pista de audio salen de **corridas separadas de FFmpeg**. Que
compartan origen de tiempo es lo único que los mantiene sincronizados. Los dos
comandos llevan `-copyts` **antes** del `-i` y, a la salida:

```
-avoid_negative_ts disabled -muxdelay 0 -muxpreload 0
```

- **Nunca `-output_ts_offset`**: con `-copyts` los PTS ya son absolutos;
  sumarlo otra vez los duplica.
- **Nunca `-start_at_zero`**: contradice `-copyts`.

Los subtítulos se extraen en otra corrida, a `.vtt`, sin `-copyts`: sus tiempos
salen del contenedor de origen. Falta verificar que no desincronice
(`TODO.md` #11).

*Lo fija:* `TranscodeCommandBuilderTests.Video_con_opciones_AV1_genera_argumentos_correctos`,
solo para el comando de video con el perfil AV1 (`-copyts` presente,
`-output_ts_offset` y `-start_at_zero` ausentes). El comando de audio, la
posición de `-copyts` y los flags de mux no tienen test.

### I2 · Las playlists se calculan, no se sirven

El `internal.m3u8` de FFmpeg **jamás llega al cliente**: se parsea solo para
conocer las duraciones reales y para saber si terminó (`#EXT-X-ENDLIST`).
Corolario: **nunca `-hls_playlist_type vod`**, porque con esa opción FFmpeg
escribe la playlist recién al cerrar y durante todo el build no hay de dónde
leer las duraciones, aunque los `.m4s` estén en disco desde el primer segundo.

Por la misma razón, *reproducible* cuenta segmentos **en disco**, no entradas de
la playlist.

*Lo fija:* ningún test. Lo sostienen el filtro de `Program.cs` y la lista
blanca de `GetHlsFileUseCase` (`system_arquitecture.md` §9).

### I3 · Los `#EXTINF` son las duraciones reales

Declarar `6.000` uniforme cuando los segmentos no lo son desfasa los subtítulos,
y el error se acumula a lo largo de la película. Con remux los segmentos cortan
en los keyframes del origen y no son uniformes; al codificar, `-force_key_frames`
pone un keyframe en cada borde, pero las duraciones igual salen de FFmpeg.

*Lo fija:* ningún test.

### I4 · `CODECS`: los dos o ninguno

> Referenciado en: `system_arquitecture.md:202`

En el master, **o se declaran video y audio, o se omite el atributo entero**.
Declarar solo el del audio no es "omitir el del video": anuncia un variant *sin*
video, hls.js espera un `BUFFER_CODECS` cuando van a llegar dos, y la segunda
pista se queda sin SourceBuffer. Pasa siempre que el codec de video no se puede
escribir: un AV1, que no declara nivel, o un H.264 copiado con un perfil o
nivel que `AvcCodecString` no reconoce.

Corolario: el string que anuncia un encoder tiene que coincidir con lo que
codifica. El de H.264 está fijo (`avc1.64001f`) porque el comando fija
`-level:v 3.1`.

*Lo fijan:* `PlaylistBuilderTests.Master_con_AV1_omite_CODECS_completo` y
`TranscodeCommandBuilderTests.Video_con_opciones_H264_genera_libx264` (nivel ↔
string).

### I5 · Asset ≠ Session

Dos conceptos, dos ciclos de vida (`system_arquitecture.md` §5.2). Mezclarlos
fue el error del diseño anterior al prototipo.

### I6 · No pasar selección de pistas significa generar todo

No significa "dejar como estaba". La ficha es la única fuente de esa decisión;
la copia que queda en el manifest es derivada y no manda. Abrir por `file_path`,
sin ficha, regenera el archivo completo.

Corolario: **el filtro se aplica en `OpenCoreAsync`, nunca en `LoadAsync`.**
`LoadAsync` solo corre cuando el asset no está en memoria; si el filtro viviera
ahí, cambiar la selección de una película ya abierta no haría nada *y no daría
error*.

*Lo fija:* ningún test.

### I7 · El asset recuerda con qué se construyó

> Referenciado en: `system_arquitecture.md:416`

Las `TranscodeOptions` salen del manifest, no de la configuración del servidor
(`system_arquitecture.md` §5.4). Las de quien abre (y el `pin`) solo se aplican
mientras el video **no se despachó**: es lo que hace que "Play después de
encode" no recodifique y anuncie AV1. `IsVideoDispatched` es el criterio único,
y lo usan la apertura y el chequeo de `/encode`.

*Lo fija:* ningún test.

### I8 · Toda salida codificada es SDR BT.709

Con cualquier encoder registrado, hoy o mañana. Una fuente HDR (PQ o HLG) se
tonemapea a SDR y nunca se copia, aunque la estrategia sea remux; toda salida
codificada, HDR o no, se etiqueta BT.709 en los frames. Quien decide el tonemap
es la fuente, no el perfil.

*Lo fijan:* `TranscodeCommandBuilderTests.Video_tonemap_solo_si_la_fuente_es_HDR_con_cualquier_encoder`
(teoría sobre `VideoEncoders.Names`) y `Video_remux_con_HDR_recodifica`. Que
`AssetBuilder` ponga `TonemapHdr` desde la fuente no tiene test.

---

## 2. Qué sigue siendo sincrónico

> Referenciado en: `system_arquitecture.md:637`

La deuda del prototipo (un `collect` sincrónico bloqueando el event loop entero)
no aplica así: `TotalBytesAsync` y cada borrado de `CollectAsync` van con
`Task.Run`. Lo que queda:

- **El recorrido del cache.** Cada apertura de un asset nuevo recorre el árbol
  entero **dos veces** (`CollectAsync` mide el total y después `EnsureAsync`
  vuelve a medirlo), esté o no lleno el cache. Si hay que recolectar,
  `CollectAsync` además lee el manifest de cada asset en serie y su
  `.last-access` con I/O sincrónico. El costo crece con la cantidad de
  segmentos (`TODO.md` #15).
- **Disco en caminos calientes.** `CountSegments` enumera el directorio de
  video en cada `GET /sessions/{id}` (cada 2 s por espectador);
  `IsPlaylistComplete` y `Prepare` leen y escriben sincrónicamente bajo el lock
  del asset; `AssetIdFor` hace stat del origen en cada alta, `/stream` y
  `/encode`.
- **SQLite.** `Microsoft.Data.Sqlite` no tiene I/O asincrónico: sus métodos
  `Async` corren sincrónicos y ocupan un hilo del pool durante la consulta.

---

## 3. Cobertura de tests

> Referenciado en: `system_arquitecture.md:750`

Lo que hay:

- `TranscodeCommandBuilderTests`: argumentos del perfil AV1, tonemap y
  etiquetado BT.709 para cada encoder registrado, H.264 (nivel ↔ `CODECS`, caja
  con `setsar=1`) y remux de una fuente HDR.
- `PlaylistBuilderTests`: el master con AV1 omite `CODECS` entero.
- `MediaPathResolverTests`: rutas con raíz, fuera de la raíz, extensión y
  existencia (sobre un directorio temporal).
- `FfprobeMediaAnalyzerTests`: `Parse` con JSON de ejemplo, detección de HDR
  por `color_transfer`.
- `SessionManagerTests`: una sesión vencida no se lee ni revive con un latido
  tardío, sigue protegiendo su asset hasta el `Prune`, y `Prune` la libera
  (reloj manual).

Cada proyecto tiene además un `UnitTest1` vacío.

Lo que falta: `AssetBuilder`, `AssetStore` y `CapacityGuard` no tienen tests, aunque
el diseño lo permite (`IFfmpegRunner`, `IMediaAnalyzer` e `IAssetStore` son
interfaces, y `AssetStore` recibe un `TimeProvider`). Tampoco el pipeline HTTP
(`system_arquitecture.md` §9): `WebApi.Tests` es un placeholder. Y **que FFmpeg
haga lo que se espera** (PTS absolutos, suma de `#EXTINF` contra la duración,
keyframes en los bordes, alineación audio/video) no lo verifica nada
automatizado.
