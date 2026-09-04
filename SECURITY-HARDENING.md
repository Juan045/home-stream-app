# Plan de hardening del backend

Cierre de fugas de información en la API pública, previo a construir el catálogo,
el detalle de contenido y el panel de administración.

Todos los hallazgos fueron verificados contra el código en la rama
`feature/multi-audio+FastAPI`. Las referencias son `archivo:línea`.

---

## Principio

**El frontend no es un límite de seguridad.** El bundle es legible y cualquier
cosa que el navegador pueda pedir, el usuario la pide con `curl`. Ocultar
endpoints en el JavaScript no protege nada.

Lo que sí protege es reducir la información que el servidor *regala* y autorizar
del lado del servidor. De ahí la regla que ordena todo este plan:

> Ningún objeto de dominio sale por la API. Todo pasa por un DTO público
> definido según la pantalla que lo consume, no según cómo está guardado
> internamente.

`app/models/schemas.py` es esa frontera. Es también la razón por la que varias de
estas fugas existen: los schemas se escribieron mirando el `Asset`, no mirando al
player.

Consecuencia útil: no hay que elegir entre "el frontend sabe todo" y "el frontend
no sabe nada". Hay dos audiencias con dos contratos. Datos como la ruta real, el
estado del cache o el stderr de FFmpeg son legítimos en un `AdminMediaItem`
detrás de autenticación; son una fuga en la respuesta al player.

---

## Resumen de hallazgos

| ID | Fuga | Severidad | Esfuerzo |
|----|------|-----------|----------|
| F0 | `MEDIA_ROOT` opcional: sin configurar, cualquier `.mp4`/`.mkv` del host es transmisible | Alta | Baja |
| F1 | El cliente envía rutas absolutas del filesystem | Alta | Alta (fase 2) |
| F2 | Los errores devuelven rutas absolutas en `detail` | Media | Baja |
| F3 | El stderr de FFmpeg viaja al navegador y se muestra en pantalla | Media | Baja |
| F4 | `strategy` expone el pipeline interno sin que nadie lo consuma | Baja | Baja |
| F5 | `/docs` y `/openapi.json` abiertos en producción | Media | Baja |
| F6 | CORS con `allow_origins=["*"]` | Media | Baja |

---

## Fase 1 — Parches puntuales

Ninguno depende de otro. Todos son de alcance acotado.

### F0 · `MEDIA_ROOT` debe ser obligatorio

**Dónde:** `app/config.py` (`MEDIA_ROOT: Path | None = None`), `app/api/routes.py:71`

**Problema.** El valor por defecto es `None`, y `_validate_path` sólo aplica la
comprobación de contención cuando `media_root is not None`. Con la configuración
por defecto, un `POST /api/v1/stream` con cualquier ruta absoluta del servidor
que termine en `.mp4` o `.mkv` se transmite. `docker-compose.yml` define
`SM_MEDIA_ROOT=/media`, así que el despliegue habitual está cubierto — pero la
seguridad depende de que alguien no olvide una variable de entorno.

**Arreglo.** Validar al arrancar: si `MEDIA_ROOT` es `None` y `DEBUG` está
apagado, abortar el arranque con un mensaje claro. Un fallo ruidoso en el
`lifespan` es preferible a un servidor abierto en silencio.

**Nota.** `Settings` no tiene campo `DEBUG`. `docker-compose.yml` pasa `DEBUG`,
pero `env_prefix` es `SM_`, así que hoy esa variable no llega a ningún lado. Hay
que agregar `DEBUG: bool = False` a `Settings` — F5 también lo necesita.

---

### F2 · Rutas absolutas en el cuerpo del error

**Dónde:** `app/api/routes.py:68` y `app/api/routes.py:75`

```python
raise ApiError(404, "file_not_found", f"El archivo no existe: {resolved}")
...
raise ApiError(400, "outside_media_root",
               f"El archivo esta fuera del directorio permitido: {media_root}")
```

**Problema.** El campo `detail` viaja íntegro al cliente. El primero devuelve la
ruta resuelta; el segundo, el `MEDIA_ROOT` del servidor. Entre los dos, un 404
alcanza para dibujar el árbol de directorios: el atacante prueba rutas y el
mensaje le confirma cuáles existen.

**Arreglo.** El slug (`file_not_found`) es lo que el cliente necesita para
reaccionar. La ruta va al log estructurado, no a la respuesta.

```python
log.warning("archivo no encontrado", path=str(resolved))
raise ApiError(404, "file_not_found", "El archivo solicitado no existe")
```

Mismo criterio para `outside_media_root`: mensaje genérico afuera, ruta adentro
del log.

**Tests.** `tests/test_api.py:89` verifica el slug, no el `detail`. Sobrevive sin
cambios. Conviene agregar uno que afirme que el `detail` **no** contiene la ruta.

---

### F3 · El stderr de FFmpeg llega al navegador

**Dónde:** `app/services/asset_builder.py:467` → `:133` → `app/api/routes.py`
(`_stream_response`, `error=asset.error`) → `static/player.html:356`

**Problema.** Cadena completa y verificada:

1. `asset_builder.py:467` guarda `artifact.error = exc.stderr`.
2. `Asset.error` (`asset_builder.py:133-136`) devuelve ese stderr.
3. `_stream_response` lo copia en `StreamResponse.error`.
4. `player.html:356` lo renderiza: `"El procesamiento falló: " + data.error`.

El stderr de FFmpeg contiene rutas absolutas del origen y del cache, la versión
del binario, la configuración de compilación y los flags exactos del comando. Es
un volcado del backend impreso en la pantalla del usuario.

**Arreglo.** Separar diagnóstico interno de mensaje público. `StreamResponse.error`
pasa a ser un slug estable (`transcode_failed`, `audio_track_failed`) que el
player pueda mostrar de forma legible. El stderr sigue guardándose en el
artefacto y en el log — que es donde sirve — y se expondrá en el DTO de admin
cuando exista.

**Tests.** Agregar uno que fuerce un fallo de FFmpeg y afirme que la respuesta no
contiene texto del stderr.

---

### F4 · `strategy` en la respuesta pública

**Dónde:** `app/models/schemas.py` (`StreamResponse.strategy`), `app/api/routes.py:88`

**Problema.** Le dice al cliente si hubo remux o transcodificación. `player.html`
no lo usa: es telemetría interna que se coló en el modelo público porque el
schema se escribió mirando el `Asset`. Caso testigo de la regla del DTO.

**Arreglo.** Sacar el campo de `StreamResponse`. Cuando exista el panel de
administración, va ahí — es información útil para diagnosticar, con la audiencia
correcta.

**Tests.** `tests/test_api.py:113` afirma `data["strategy"] == "transcode"`. Hay
que moverlo: la decisión de estrategia ya está cubierta en
`tests/test_media_analyzer.py:106`, que es donde corresponde probarla.

---

### F5 · `/docs` y `/openapi.json` abiertos

**Dónde:** `app/main.py:125`

```python
app = FastAPI(title="Stream Media", lifespan=lifespan)
```

**Problema.** Sin `docs_url` ni `openapi_url` explícitos, FastAPI publica el
esquema completo de la API: todos los endpoints, todos los campos, todos los
tipos. Es el mapa del backend servido gratis.

**Arreglo.** Condicionar al flag `DEBUG` que introduce F0:

```python
_debug = get_settings().DEBUG
app = FastAPI(
    title="Stream Media",
    lifespan=lifespan,
    docs_url="/docs" if _debug else None,
    redoc_url=None,
    openapi_url="/openapi.json" if _debug else None,
)
```

**Compatibilidad con la generación de tipos del frontend.** El plan de generar
tipos TypeScript desde OpenAPI sigue funcionando: se generan en **build time**
contra el servidor local con `SM_DEBUG=1`, y el artefacto que se despliega no
expone el esquema. El contrato tipado se conserva; el mapa público no.

---

### F6 · CORS abierto

**Dónde:** `app/main.py:127-133`

```python
allow_origins=["*"], allow_methods=["*"], allow_headers=["*"]
```

**Problema.** El SPA se va a servir desde el mismo origen que la API, así que el
comodín no hace falta. Cuando el panel de administración tenga autenticación por
cookie, además va a estorbar.

**Arreglo.** Lista explícita de orígenes desde configuración
(`SM_ALLOWED_ORIGINS`), con el dev server de Vite (`http://localhost:5173`)
incluido sólo cuando `DEBUG` está encendido. Acotar también `allow_methods` a lo
que la API realmente usa.

---

## Fase 2 — Catálogo con identificadores opacos (F1)

Excede el alcance de "parche", pero es el arreglo real de la fuga más grande y
conviene dejarlo anotado acá porque F0 y F2 son mitigaciones de lo mismo.

**Problema.** Hoy el contrato es:

```json
POST /api/v1/stream  {"file_path": "/media/Peliculas/Alien.1979.mkv"}
```

El navegador aprende que hay un filesystem, cómo está organizado, y que la ruta
*es* la identidad del contenido. `_validate_path` (`routes.py:48`) es buena
defensa, pero está defendiendo una superficie que no debería existir.

**Arreglo.** Un indexador de biblioteca en el backend, y el path traversal
desaparece del API público en lugar de atajarse en cada request:

```
GET  /api/v1/library             -> [{media_id, title, duration, poster}]
GET  /api/v1/library/{media_id}  -> detalle
POST /api/v1/stream {media_id}   -> sesión + master_url
```

El `media_id` es opaco y lo asigna el backend. La ruta nunca sale del servidor y
`_validate_path` pasa a ser una validación interna del indexador.

**No aplica a `asset_id`.** Ya está bien resuelto: `asset_store.py:35` es un SHA1
truncado de `(ruta, mtime_ns, tamaño)`. No es reversible ni enumerable.

---

## Fuera de alcance

**Las URLs de segmentos HLS seguirán siendo visibles.** El manifest las expone
por definición — el cliente tiene que poder pedirlas. Se ve que el video va
separado del audio y cuántas pistas hay. Eso es inherente al formato, no un
descuido.

Si en algún momento hace falta que además no sean compartibles ni enumerables, el
camino es firmar el prefijo `/hls/{asset_id}/` por sesión. No ofuscar la
estructura.

---

## Orden sugerido

1. **F0** — agregar `DEBUG` a `Settings` y hacer `MEDIA_ROOT` obligatorio. Va
   primero porque F5 y F6 dependen del flag.
2. **F2, F3, F4** — saneo de respuestas. Independientes entre sí.
3. **F5, F6** — exposición y CORS.
4. **Fase 2** — catálogo, cuando arranque el frontend nuevo.

Autenticación y panel de administración van al final, cuando esté claro qué
gestiona. La autorización se define endpoint por endpoint en el backend: que el
frontend no dibuje el botón de admin es cosmético; lo que decide es el 403.

---

## Verificación

Cada punto debería quedar cubierto por un test, no sólo por inspección:

- [ ] El arranque falla si `MEDIA_ROOT` no está configurado y `DEBUG=0`.
- [ ] Ningún `detail` de error contiene una ruta absoluta.
- [ ] Un fallo de FFmpeg no filtra stderr en la respuesta HTTP.
- [ ] `StreamResponse` no tiene campos que el player no consuma.
- [ ] `GET /openapi.json` responde 404 con `DEBUG=0`.
- [ ] La respuesta de CORS no lleva `Access-Control-Allow-Origin: *`.
