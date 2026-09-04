import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// El SPA se sirve desde el mount /static que app/main.py ya tiene, sin tocar
// el backend. En dev queda en la raiz del dev server; en build, colgado de
// /static/app/ para que las URLs de los assets resuelvan.
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
    proxy: {
      '/api': 'http://localhost:8000',
      '/hls': 'http://localhost:8000',
    },
  },
}))
