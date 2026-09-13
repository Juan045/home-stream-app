# Decisión de stack para el frontend

Elección de lenguaje y herramientas para reemplazar `static/player.html` por una
aplicación con catálogo, detalle de contenido, reproductor y panel de
administración.

Complemento de [`SECURITY-HARDENING.md`](SECURITY-HARDENING.md), que cubre los
cambios de backend que este frontend asume.

---

## Decisión

**Lenguaje: TypeScript.**

| Capa | Elección | Por qué |
|---|---|---|
| Lenguaje | TypeScript | Los tipos de la API se generan desde el `/openapi.json` de FastAPI. El backend queda como única fuente de verdad del contrato. |
| Build | Vite | Compila a estático. Lo sirve el `StaticFiles` que ya existe. Un contenedor, un puerto, un origen. |
| UI | React | Catálogo, detalle, player y admin son ~5 rutas con estado. El HTML de Claude Design traduce a JSX casi línea por línea. |
| Rutas | React Router | Con `lazy()` para el admin: su código no viaja en el bundle principal. |
| Datos | TanStack Query | Caché e invalidación para el catálogo. Con una sola pantalla no hacía falta; con cuatro, sí. |

```
npm create vite@latest frontend -- --template react-ts
```

El directorio `frontend/` ya existe y está vacío. Es el lugar.

---

## Por qué TypeScript, concretamente

El argumento fuerte no es "tipos son buenos". Es que FastAPI ya publica el
esquema completo de la API en `/openapi.json`, y con `openapi-typescript` esos
tipos se generan en un paso de build. Si cambia un campo en
`app/models/schemas.py`, el frontend **deja de compilar** en vez de romperse en
runtime.

Con el plan de hardening en marcha esto pesa más: F4 saca `strategy` de
`StreamResponse` y F3 cambia la forma del campo `error`. Son exactamente los
cambios de los que querés enterarte en el build y no cuando el usuario abre la
página.

---

## Alternativas descartadas

### Next.js

Aporta SSR, React Server Components, rutas de API, ISR y optimización de
imágenes. De esa lista este proyecto no usa ninguna:

- **SSR no aplica.** El player es 100% cliente: hls.js necesita `MediaSource` y
  un `<video>` real en el DOM, que en el servidor no existen. Todo componente
  relevante sería `'use client'`.
- **Rutas de API no aplican.** Ya están en FastAPI. Duplicarlas da dos backends y
  la tentación de mover lógica al lado equivocado.
- **No hay SEO que defender.** Es un servicio auto-hospedado.

Costo real: Next necesita un proceso Node corriendo. Hoy hay **un** contenedor;
pasarían a ser dos más un reverse proxy. La alternativa (`next export`) es
exactamente Vite con más dependencias.

### Astro

Su valor es enviar cero JS en sitios de contenido, con islas interactivas donde
hagan falta. Acá **la isla es todo**: player, selectores de pista, polling de
progreso, heartbeat. Y el catálogo no es contenido estático — sale de un índice
que el backend construye y que cambia al agregar películas, así que igual sería
un fetch de cliente dentro de una isla de React. No es una mala herramienta;
apunta a un problema que este proyecto no tiene.

### Svelte

**Es la única alternativa que vale reconsiderar.** Menos ceremonia y bundle más
chico para una UI de este tamaño. Se descarta por dos razones blandas, no
técnicas: el ecosistema de React es más grande, y el HTML que produce Claude
Design mapea de forma más directa a JSX. Si preferís Svelte, el resto del stack
(Vite, TypeScript, generación de tipos, integración con el backend) no cambia en
nada.

### Vanilla TypeScript

Alcanzaría para el player solo. Con catálogo, detalle y admin, el estado
compartido y los estados de carga se vuelven manuales rápido.

### Un BFF como proceso separado

Se evaluó insertar un **Backend for Frontend** entre el SPA y FastAPI. Se
descarta: el patrón resuelve varios clientes tironeando de una API genérica, o un
frontend hablando con varios servicios río abajo. Acá hay un cliente y un
backend monolítico, y el BFF agregaría un segundo proceso, un hop de red y
duplicación de modelos.

Sobre todo: **el BFF no puede inventar el catálogo.** El mapa `media_id → ruta`
necesita enumerar `MEDIA_ROOT`; ponerlo en el BFF significa darle acceso al
filesystem de medios y duplicar lo que ya vive en `asset_store.py`.

La mitad buena del patrón sí se adopta, como capa y no como componente: la regla
del DTO documentada en `SECURITY-HARDENING.md`. `app/models/schemas.py` **es** el
BFF de este proyecto.

**Cuándo reconsiderarlo:** si aparece un segundo cliente con forma distinta (app
de TV, móvil), o si la autenticación del admin pasa a ser OAuth/OIDC federado —
ahí el BFF como cliente confidencial es la recomendación vigente para apps de
navegador.

---

## Integración con el backend

### Desarrollo

Vite dev server en `:5173` con proxy de `/api` y `/hls` hacia `:8000`. Así no hay
CORS en desarrollo, y en producción tampoco porque es mismo origen. Ver F6 del
plan de hardening: `allow_origins` pasa a lista explícita, con `localhost:5173`
incluido sólo cuando `DEBUG` está encendido.

### Producción

`npm run build` produce `static/app/`, que sirve FastAPI montado en la raíz. En
el `Dockerfile`, un stage de `node:22` que compila y lo copia; la imagen final no
lleva Node.

### Una página por vista (MPA), no un SPA con router

El build emite un `index.html` por ruta:

```
static/app/index.html          ->  /          galeria
static/app/new/index.html      ->  /new/      alta de medio
static/app/player/index.html   ->  /player/   reproductor (?file | ?session | ?src)
```

Son tres entry points de Rollup (`build.rollupOptions.input`), cada uno con su
`src/*.tsx` que monta un componente. **No hay router del lado del cliente:** cada
vista es un documento propio, así que no hay `react-router-dom` ni `basename` ni
nada que resolver en el navegador.

El motivo es que cada ruta pasa a ser un archivo en disco, y entonces el backend
no necesita ningún fallback al `index.html` — el mount con `html=True` alcanza. El
diseño anterior (un solo `index.html` + router) obligaba a devolver ese archivo
para rutas que no existen en disco, y al no estar hecho, el router terminó
leyendo la query en vez de la ruta.

Efecto secundario medible: hls.js pesa ~600 KB y sólo lo descarga `/player/`. La
galería bajó de 845 KB a ~203 KB.

`appType: 'mpa'` desactiva el fallback del dev server, para que una ruta que no
existe dé 404 igual que en producción. Y un plugin de ocho líneas replica el 307
con barra final que hace Starlette, así que la misma URL funciona en los dos.

### Orden de montaje — importante

En `app/main.py` todo el registro de rutas vive en un solo bloque al final del
módulo, y ese bloque es el orden en que Starlette resuelve. El mount de `/` va
**último** porque matchea todo. Ver *Espacio de URLs* en `CLAUDE.md`, que tiene
la tabla completa y las tres reglas que no hay que reordenar.

### Generación de tipos

```
SM_DEBUG=1 uvicorn app.main:app  →  openapi-typescript  →  frontend/src/api/types.ts
```

Se corre en build time contra el servidor local. Con `DEBUG=0` el esquema no se
publica (F5), así que el contrato tipado se conserva sin exponer el mapa de la
API.

---

## Notas de hls.js para este backend

Estas son específicas de cómo genera los artefactos este proyecto y no se deducen
de la documentación general de hls.js.

**El cambio de idioma es `hls.audioTrack = n`, y nada más.** No se recarga el
manifest, no se pierde `currentTime`, no se reinicia FFmpeg. El master declara
las pistas con `#EXT-X-MEDIA:TYPE=AUDIO` justamente para eso.

> ⚠️ El ejemplo de la sección 5 de la skill de HLS **no aplica acá**: hace
> `POST /jobs/{id}/select` y después `loadHls(data.hls_url)` para cambiar de
> audio. Ese es el diseño anterior, ya reemplazado. Si aparece ese patrón en
> código generado, está mal.

**No configurar ABR.** El master declara un solo `#EXT-X-STREAM-INF`
(`app/services/playlist.py:192`). `capLevelToPlayerSize`, `abrBandWidthFactor` y
los selectores de calidad son ruido: no hay entre qué elegir.

**Tolerancia para el modo EVENT.** Mientras el build corre, la playlist se sirve
como EVENT y crece. Conviene subir `maxBufferLength` y `fragLoadingMaxRetry`: el
cliente va a pedir segmentos que todavía no existen.

**Subtítulos como `<track>`.** Se sirven como `.vtt` estáticos, fuera del
pipeline de audio. Sus tiempos son absolutos, igual que los de los segmentos.

---

## Rutas previstas

| Ruta | Consume | Notas |
|---|---|---|
| `/` | `GET /api/v1/library` | Catálogo. Requiere fase 2 del hardening. |
| `/media/:id` | `GET /api/v1/library/{media_id}` | Detalle + botón de reproducir. |
| `/watch/:id` | `POST /api/v1/stream` | Player. Es lo que hoy hace `player.html`. |
| `/admin` | endpoints con auth | `lazy()`. Autorización server-side, el 403 es lo que decide. |

---

## Decisiones pendientes

- [ ] **React o Svelte.** Todo lo demás del stack es igual en ambos casos.
- [ ] **Orden de arranque:** fase 1 del hardening primero, o andamiaje del
      frontend en paralelo. Son independientes.
- [ ] **Diseño.** El HTML de Claude Design se traduce a componentes; conviene que
      incluya los estados reales (build en progreso, sin subtítulos, fallo de
      FFmpeg, una sola pista de audio) y no sólo el caso ideal.
