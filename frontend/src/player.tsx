/** Entry del reproductor: se sirve en `/player/`, con `?media`, `?file`, `?session`
 *  o `?src` como parametros. */
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// telon.css antes que la pagina: el CSS de la pagina pisa al comun.
import './telon.css'
import PlayerPage from './pages/player/PlayerPage'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <PlayerPage />
  </StrictMode>,
)
