import { resolve } from 'node:path'
import react from '@vitejs/plugin-react'
import { defineConfig, type Plugin } from 'vite'

// Las paginas que viven en un subdirectorio, o sea las que tienen barra final.
const PAGES = ['/new', '/player']

/**
 * Iguala el dev server a como sirve Starlette.
 *
 * Vite resuelve /new/ a new/index.html pero /new pelado le da 404, mientras que
 * el mount de FastAPI redirige con un 307. Sin esto la misma URL anda en
 * produccion y falla en desarrollo, que es justo la asimetria que este diseno
 * vino a sacar.
 */
function trailingSlash(): Plugin {
  return {
    name: 'trailing-slash',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const [path, query] = (req.url ?? '/').split('?')
        if (!PAGES.includes(path)) return next()
        res.writeHead(307, { Location: `${path}/${query ? `?${query}` : ''}` })
        res.end()
      })
    },
  }
}

// Tres paginas, una por vista: el build escribe un index.html por ruta y el
// backend las sirve como archivos. Eso es lo que hace que /new/ sobreviva un
// refresh sin que nadie tenga que devolver el index.html a mano.
//
// Se monta en la raiz, asi que `base` es '/' tanto en dev como en build.
//
// Las dos variables de abajo existen solo para el servicio `frontend` del
// docker-compose. Corriendo en el host no hace falta ninguna.
const apiTarget = process.env.SM_API_TARGET ?? 'http://localhost:8000'

// Un bind mount de Windows no propaga eventos de archivo al contenedor: sin
// polling el hot reload no se entera de nada. En el host es al reves, el
// polling es puro gasto de CPU.
const watch = process.env.VITE_POLL ? { usePolling: true, interval: 300 } : undefined

export default defineConfig({
  base: '/',
  plugins: [react(), trailingSlash()],
  // Sin fallback al index.html: una ruta que no existe tiene que dar 404 en el
  // dev server igual que servida por FastAPI. El default ('spa') las resolveria
  // todas y esconderia el error hasta produccion.
  appType: 'mpa',
  build: {
    // Fuera de frontend/: lo escribe donde StaticFiles lo encuentra.
    // Vacia solo static/app, nunca static/player.html.
    outDir: '../static/app',
    emptyOutDir: true,
    rollupOptions: {
      input: {
        main: resolve(__dirname, 'index.html'),
        new: resolve(__dirname, 'new/index.html'),
        player: resolve(__dirname, 'player/index.html'),
      },
    },
  },
  server: {
    watch,
    proxy: {
      '/api': apiTarget,
      '/hls': apiTarget,
    },
  },
})
