/** Entry de la galeria: se sirve en `/`. */
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// telon.css antes que la pagina: el CSS de la pagina pisa al comun.
import './telon.css'
import GalleryPage from './pages/gallery/GalleryPage'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <GalleryPage />
  </StrictMode>,
)
