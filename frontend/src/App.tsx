/**
 * Router de la aplicacion. Una ruta por vista, y nada mas.
 *
 * El `basename` sale de la base de Vite: en desarrollo el SPA cuelga de la
 * raiz y en el build de /static/app/ (vite.config.ts). Sin eso las rutas no
 * resuelven cuando lo sirve FastAPI, y el sintoma es una pagina en blanco.
 */
import { BrowserRouter, Route, Routes, useLocation } from 'react-router-dom'
import AddMediaPage from './pages/add/AddMediaPage'
import GalleryPage from './pages/gallery/GalleryPage'
import PlayerPage from './pages/player/PlayerPage'

/**
 * Todo lo que no es /watch: el catalogo, salvo que la query traiga uno de los
 * tres modos de entrada del player.
 *
 * `?add` entra al alta por la misma puerta, y por la misma razon: el mount de
 * /static no reescribe al index.html, asi que /media/new solo resuelve en el
 * dev server de Vite. Sobre FastAPI la unica URL que existe de verdad es el
 * index.html, y la query es lo que sobrevive un refresh.
 *
 * Renderiza el player en el lugar en vez de redirigir a /watch, y eso es a
 * proposito. Servido por FastAPI se entra por `/static/app/index.html`, que no
 * es ninguna de las rutas declaradas, y ahi hay dos razones para no tocar la
 * URL: redirigir perderia la query, y `/static/app/watch` no sobrevive un
 * refresh porque el mount de main.py no reescribe al index.html.
 */
function Home() {
  const { search } = useLocation()
  const params = new URLSearchParams(search)
  if (params.has('add')) return <AddMediaPage />

  const isPlayer =
    params.has('file') || params.has('session') || params.has('src')

  return isPlayer ? <PlayerPage /> : <GalleryPage />
}

export default function App() {
  return (
    <BrowserRouter basename={import.meta.env.BASE_URL}>
      <Routes>
        <Route path="/watch" element={<PlayerPage />} />
        {/* Sin boton que lleve aca: se entra escribiendo la URL. */}
        <Route path="/media/new" element={<AddMediaPage />} />
        <Route path="*" element={<Home />} />
      </Routes>
    </BrowserRouter>
  )
}
