/** Entry del reproductor: se sirve en `/player/`, con `?file`, `?session` o
 *  `?src` como parametros. */
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import PlayerPage from './pages/player/PlayerPage'
import './theme.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <PlayerPage />
  </StrictMode>,
)
