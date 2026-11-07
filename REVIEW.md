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

> Referenciado en: `system_arquitecture.md:212`

En el master, **o se declaran video y audio, o se omite el atributo entero**.
Declarar solo el del audio no es "omitir el del video": anuncia un variant *sin*
video, hls.js espera un `BUFFER_CODECS` cuando van a llegar dos, y la segunda
pista se queda sin SourceBuffer. Pasa siempre que el codec de video no se puede
escribir: un AV1, que no declara nivel, un H.264 de biblioteca o un H.264 copiado
con un perfil o nivel que `AvcCodecString` no reconoce.

Corolario: el string que anuncia un encoder depende de sus opciones y tiene que
coincidir con lo que codifica. H.264 en vivo declara `avc1.64001f` porque el
comando fija `-level:v 3.1`. H.264 de biblioteca lo omite por dos razones: por
encima de 720p el comando fija `-level:v 4.1`, y con `ForceTranscode` el prewarm
pudo copiar un audio cuyo perfil no coincide con el `mp4a.40.2` que declararía el
master.

*Lo fijan:* `PlaylistBuilderTests.Master_con_AV1_omite_CODECS_completo`,
`Master_con_H264_en_vivo_declara_video_y_audio` y
`Master_con_H264_de_biblioteca_omite_CODECS_completo`, y en
`TranscodeCommandBuilderTests`, `Video_con_opciones_H264_genera_libx264`,
`Video_H264_de_biblioteca_usa_nivel_4_1_y_omite_CODECS`,
`Video_H264_sin_tope_de_altura_conserva_nivel_3_1_y_CODECS` y
`Video_H264_anuncia_el_nivel_que_codifica` (nivel ↔ string).

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

> Referenciado en: `system_arquitecture.md:234`, `system_arquitecture.md:300`, `system_arquitecture.md:463`

Las `TranscodeOptions` salen del manifest, no del ajuste de códec
(`system_arquitecture.md` §5.4). `/stream` y `/encode` siempre pasan las de su rol,
pero la estrategia, las opciones y el `pin` de quien abre solo se aplican mientras
el video **no se despachó**: es lo que hace que "Play después de encode" no
recodifique y anuncie el códec de biblioteca, y que cambiar el ajuste no toque lo ya
generado. `IsVideoDispatched` es el criterio único, y lo usan la apertura y el
chequeo de `/encode`.

Dos casos con el video sin despachar:

- **Asset pineado:** un encode interrumpido no cambia de perfil con un Play, solo
  con otro `/encode`.
- **Prewarm:** no trae estrategia ni opciones, así que no cambia ninguna de las dos.

Corolario: un build relanzado puede usar otro códec, así que `PrepareBuild` borra
los segmentos y la playlist del anterior antes de marcar `building`.

*Lo fijan:* `AssetBuilderOptionsTests` (perfil, estrategia, pin y prewarm, con
fakes) y `AssetStorePrepareBuildTests`. Que `StartStreamUseCase` y
`EncodeMediaUseCase` pasen el perfil del ajuste no tiene test.

### I8 · Toda salida codificada es SDR BT.709

Con cualquier encoder registrado, hoy o mañana. Una fuente HDR (PQ o HLG) se
tonemapea a SDR y nunca se copia, aunque la estrategia sea remux; toda salida
codificada, HDR o no, se etiqueta BT.709 en los frames. Quien decide el tonemap
es la fuente, no el perfil.

*Lo fijan:* `TranscodeCommandBuilderTests.Video_tonemap_solo_si_la_fuente_es_HDR_con_cualquier_encoder`
(teoría sobre `VideoEncoders.Names`), `Video_remux_con_HDR_recodifica` y
`AssetBuilderOptionsTests.El_tonemap_lo_decide_la_fuente`.

---

## 2. Qué sigue siendo sincrónico

> Referenciado en: `system_arquitecture.md:715`

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

> Referenciado en: `system_arquitecture.md:829`

Lo que hay:

- `TranscodeCommandBuilderTests`: argumentos del perfil AV1, tonemap y
  etiquetado BT.709 para cada encoder registrado, H.264 en vivo y de biblioteca
  (nivel ↔ `CODECS`, caja con `setsar=1`) y remux de una fuente HDR.
- `TranscodeProfilesTests`: la matriz de perfiles por rol y códec, que cada uno
  genere argumentos con su encoder y que un códec desconocido lance.
- `PlaylistBuilderTests`: el master con AV1 y con H.264 de biblioteca omite `CODECS`
  entero, y con H.264 en vivo declara video y audio.
- `EncodeStatusTests`: `video_codec` informa el códec real (el del origen si se
  copió, el del perfil si se recodificó) y nada sin opciones ni asset.
- `AssetBuilderOptionsTests`: con fakes, I7 sobre perfil, estrategia y pin, el
  prewarm y el tonemap según la fuente.
- `AssetStorePrepareBuildTests`: un build relanzado borra segmentos y playlist
  anteriores, y conserva los subtítulos.
- `AssetBuilderErrorPersistenceTests`: con fakes, cada fallo guarda su stderr en el
  manifest, un subtítulo fallido conserva el motivo al reabrir sin reintentarse, y
  video y audio lo pierden al volver a `pending`.
- `AssetStoreManifestErrorsTests`: los errores sobreviven a escribir y leer el
  manifest, con sus claves en disco, y un manifest sin esos campos se sigue leyendo.
- `AssetBuilderQueueTests`: con fakes y un solo slot, un artefacto en `building` figura
  en cola hasta conseguir el slot, y un subtítulo en curso no, aunque no reporte avance.
- `ColdArtifactStateTests`: estado de un artefacto fuera de memoria según el manifest y
  el disco; `ready` y `failed` no leen el disco, y `building` sin ENDLIST es `interrupted`.
- `MediaArtifactsTests`: estado por artefacto en memoria y fuera de ella: avance, cola,
  motivo del fallo, códec real, pistas ignoradas y qué carpeta se mira en cada caso.
- `GetMediaArtifactsUseCaseTests`: con fakes, la memoria manda sobre el manifest, y leer
  no toca `.last-access`, no reescribe el manifest, no abre el asset ni lanza FFmpeg.
- `MediaArtifactsContractTests`: el JSON del estado por artefacto, con sus `null`.
- `AssetStoreTotalBytesTests`: sobre un directorio real, suma los archivos anidados de
  todos los assets, incluidos `manifest.json` y `.last-access`, y un cache vacío da 0.
- `CacheUsageContractTests`: el JSON del uso del cache, con valores que no entran en 32 bits.
- `UpdateEncodingSettingsUseCaseTests`: un códec inválido lanza y no guarda; un par
  válido se guarda.
- `SqliteEncodingSettingsRepositoryTests`: sobre SQLite real, el valor por defecto
  sin fila, una sola fila, el valor que persiste y la tabla en una base existente.
- `EncodingSettingsContractTests`: el JSON del ajuste en snake_case, sin servidor.
- `ImageFormatTests`: JPEG y WebP por su firma; PNG, GIF, SVG, otro RIFF, vacío o
  cortado dan `null`.
- `MediaImageUseCasesTests`: con fakes y reloj fijo, el orden de las reglas al guardar
  (ficha, vacío, tope de 1 MiB exacto, formato), el tipo detectado y la hora del reloj;
  leer sin imagen da `image_not_found`, y borrar exige la ficha pero no la imagen.
- `SqliteMediaImageRepositoryTests`: sobre SQLite real, bytes e instante exactos, upsert
  en una sola fila, roles y fichas independientes, borrado idempotente, la tabla en una
  base existente y la clave foránea a `media`.
- `MediaImagesContractTests`: la URL con `?v=` en milisegundos, el JSON `{ url }` del PUT y
  el status de cada `ImageException` en el manejador de errores, sin servidor.
- `MediaPathResolverTests`: rutas con raíz, fuera de la raíz, extensión y
  existencia (sobre un directorio temporal).
- `FfprobeMediaAnalyzerTests`: `Parse` con JSON de ejemplo, detección de HDR
  por `color_transfer`.
- `SessionManagerTests`: una sesión vencida no se lee ni revive con un latido
  tardío, sigue protegiendo su asset hasta el `Prune`, y `Prune` la libera
  (reloj manual).

Cada proyecto tiene además un `UnitTest1` vacío.

Lo que falta: del resto de `AssetBuilder` y `AssetStore` (el GC, los builds y el
resto del manifest) no hay tests, y `CapacityGuard` no tiene ninguno, aunque el diseño lo
permite (`IFfmpegRunner`, `IMediaAnalyzer` e `IAssetStore` son interfaces, y
`AssetStore` recibe un `TimeProvider`). Tampoco que los use cases de `/stream` y
`/encode` lean el ajuste, ni el pipeline HTTP (`system_arquitecture.md` §9): routing,
status codes y manejo de errores. La página `/settings/` y el resto del frontend no
tienen tests. Y **que FFmpeg haga lo que se espera** (PTS absolutos, suma de
`#EXTINF` contra la duración, keyframes en los bordes, alineación audio/video) no lo
verifica nada automatizado.
