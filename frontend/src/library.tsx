/** Entry de la biblioteca del admin: se sirve en `/library/`. */
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// telon.css antes que la pagina: el CSS de la pagina pisa al comun.
import './telon.css'
import LibraryPage from './pages/add/LibraryPage'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <LibraryPage />
  </StrictMode>,
)
