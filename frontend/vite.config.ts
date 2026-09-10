import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// El SPA se sirve desde el mount /static que app/main.py ya tiene, sin tocar
// el backend. En dev queda en la raiz del dev server; en build, colgado de
// /static/app/ para que las URLs de los assets resuelvan.
//
// Las dos variables de abajo existen solo para el servicio `frontend` del
// docker-compose. Corriendo en el host no hace falta ninguna.
const apiTarget = process.env.SM_API_TARGET ?? 'http://localhost:8000'

// Un bind mount de Windows no propaga eventos de archivo al contenedor: sin
// polling el hot reload no se entera de nada. En el host es al reves, el
// polling es puro gasto de CPU.
const watch = process.env.VITE_POLL ? { usePolling: true, interval: 300 } : undefined

export default defineConfig(({ command }) => ({
  base: command === 'build' ? '/static/app/' : '/',
  plugins: [react()],
  build: {
    // Fuera de frontend/: lo escribe donde StaticFiles lo encuentra.
    // Vacia solo static/app, nunca static/player.html.
    outDir: '../static/app',
    emptyOutDir: true,
  },
  server: {
    watch,
    proxy: {
      '/api': apiTarget,
      '/hls': apiTarget,
    },
  },
}))
