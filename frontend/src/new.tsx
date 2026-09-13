/** Entry del alta de medios: se sirve en `/new/`. */
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import AddMediaPage from './pages/add/AddMediaPage'
import './theme.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AddMediaPage />
  </StrictMode>,
)
