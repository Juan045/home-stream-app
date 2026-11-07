/** Entry del ajuste de codificacion: se sirve en `/settings/`. */
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// telon.css antes que la pagina: el CSS de la pagina pisa al comun.
import './telon.css'
import EncodingSettingsPage from './pages/add/EncodingSettingsPage'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <EncodingSettingsPage />
  </StrictMode>,
)
