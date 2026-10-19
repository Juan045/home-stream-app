/** Entry del alta de medios: se sirve en `/new/`. */
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// telon.css antes que la pagina: el CSS de la pagina pisa al comun.
import './telon.css'
import AddMediaPage from './pages/add/AddMediaPage'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AddMediaPage />
  </StrictMode>,
)
