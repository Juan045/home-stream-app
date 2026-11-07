# Stream Media

Servicio auto-hospedado de streaming: recibe un video, detecta codec, pistas de audio y subtítulos, y lo sirve al navegador por HLS (fMP4/CMAF). Cada pista de audio va en su propia rendition, así que cambiar de idioma no reinicia la reproducción. La playlist se entrega como `EVENT` mientras FFmpeg procesa, para poder reproducir a los pocos segundos.

- **Backend:** .NET 10 (C#), ASP.NET Core Web API, SQLite. FFmpeg y ffprobe se invocan como procesos del sistema.
- **Frontend:** React 19 + TypeScript + Vite + hls.js, en `frontend/`. Se compila a `wwwroot` y ASP.NET Core lo sirve en `/` (galería), `/library/`, `/new/`, `/edit/` y `/settings/` (admin) y `/player/`. Usa el rediseño **Telón**: los tokens comunes están en `frontend/src/telon.css` y los mockups en `mockups/`.
- **Puertos:** la app (Docker o `dotnet run`) en `http://localhost:8000` y Vite en desarrollo en `http://localhost:5173`.

## Documentación

| Documento | Para qué |
|---|---|
| `README.md` | Estado del proyecto, puesta en marcha, configuración y lista de endpoints |
| `system_arquitecture.md` | Arquitectura implementada: componentes, ciclos de vida, concurrencia, URLs y decisiones (D1–D17) |
| `TODO.md` | Brechas conocidas |
| `REVIEW.md` | Invariantes, deuda de rendimiento y cobertura de tests |
| `.claude/Feature-*.md` | Planes de features ya hechas, no especificación. Ante una contradicción, manda el código |

## Arquitectura en corto

Detalle completo en `system_arquitecture.md`. Lo que no hay que romper:

- **Capas.** Hay cuatro proyectos: `src/StreamMedia.{Domain,Application,Infrastructure,WebApi}`, cada uno con su proyecto de tests en `tests/`. Application solo conoce a Domain y declara las interfaces (`IAssetStore`, `IFfmpegRunner`, `IMediaAnalyzer`, `IMediaRepository`, `IEncodingSettingsRepository`, `ICatalogSource`). Infrastructure las implementa y WebApi es solo el borde HTTP.
- **Un solo proceso.** No hay worker, cola ni broker. Los builds son `Task`s coordinadas por dos `SemaphoreSlim` en `AssetBuilder`: uno por asset y otro global de `Ffmpeg:MaxConcurrent`.
- **Tres ciclos de vida distintos.**
  - `Media` es la ficha, en SQLite.
  - `Asset` son los artefactos en disco más su estado en memoria. Su id sale de `(ruta, mtime, tamaño)` y lo borra el GC por LRU.
  - `Session` es el espectador, solo en memoria. No es dueña de nada: solo protege su asset del GC.

  `Media → Asset` es una referencia débil a propósito. `AssetBuilder` **no conoce la base de datos**.
- **`manifest.json` es la única fuente de verdad que sobrevive a un reinicio.** Se escribe de forma atómica (archivo temporal + rename), bajo el lock del asset.
- **FFmpeg escribe directo en el cache, y el origen nunca se escribe** (montaje `:ro`).
- **`internal.m3u8` no se sirve nunca.** Las playlists que ve el cliente se calculan en cada request.
- **No existe un endpoint para cambiar de pista ni para hacer seek.** Lo resuelve hls.js en el cliente; si aparece una ruta así, algo se rompió conceptualmente.
- **El frontend es MPA:** un `index.html` por página, sin fallback a un `index.html` global.
- **Tests:** xUnit. Cubren sobre todo la lógica pura (`PlaylistBuilder`, `TranscodeCommandBuilder`, `VideoEncoders`, `TranscodeProfiles`, `SessionManager`, etc.). De la orquestación solo están las opciones de `AssetBuilder` y `AssetStore.PrepareBuild`; el detalle está en `REVIEW.md` §3.

## Reglas para agentes

1. **Búsqueda de skills:** antes de empezar una tarea que requiera escribir, buscar si sirve alguna de las skills (`.claude/skills/`: `ponytail`, `dotnet-best-practices`, `REACT`, `HLS`, `FFMPEG`, `telon-restyle`).
2. **Carga obligatoria de Ponytail:** cargar siempre la skill `ponytail` antes de escribir código, y escribir con ella. En código .NET, cargar además `dotnet-best-practices`.
3. **Git solo de lectura:** el agente no toca git salvo para leer (`git log`, `git status`, `git diff`, `git branch`, etc.). No agrega archivos, no hace commits, no crea ramas ni hace merge: eso lo maneja el usuario.
4. **No ejecutar herramientas externas:** el agente no ejecuta `dotnet`, `ffmpeg`, `docker`, etc. Al terminar, indica en su respuesta cómo ejecutar lo que haga falta (compilar, testear, levantar).
5. **Responsabilidad mínima y SOLID:** cada componente, archivo y namespace tiene una sola responsabilidad.
   - **Componentes:** una clase, un use case o un componente de React hace una sola cosa. Si para describirlo hace falta un "y", se separa. La tabla "Responsabilidades" de `system_arquitecture.md` (§4) dice de qué es dueño cada componente del backend y de qué no sabe nada; no se le agrega lo que no le corresponde.
   - **Archivos:** un tipo público por archivo en C#; un componente o hook por archivo en el frontend (`hooks/useX.ts`, `X.tsx`).
   - **Namespaces y carpetas:** el namespace coincide con la carpeta y agrupa por feature (`Catalog/`, `Media/`, `Encoder/`…). Ningún namespace mezcla features ni capas.
   - **SOLID:**
     - *Single responsibility:* lo de arriba.
     - *Open/closed:* extender con una entrada nueva en vez de tocar el núcleo, como el registro `VideoEncoders` (D8).
     - *Liskov:* una implementación de una interfaz cumple el contrato completo, sin `NotImplementedException`.
     - *Interface segregation:* interfaces chicas, pensadas para quien las consume.
     - *Dependency inversion:* Application depende de sus propias interfaces, nunca de Infrastructure.
   - **Sin sobre-abstraer:** la inversión de dependencias se aplica en los bordes (disco, procesos, red, base de datos). Dentro de una capa no se crea una interfaz con una sola implementación ni una capa "por si acaso"; manda ponytail.
