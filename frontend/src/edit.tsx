/** Entry de la edicion de fichas: se sirve en `/edit/`. */
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// telon.css antes que la pagina: el CSS de la pagina pisa al comun.
import './telon.css'
import EditMediaPage from './pages/add/EditMediaPage'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <EditMediaPage />
  </StrictMode>,
)
