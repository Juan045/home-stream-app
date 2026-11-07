/** La Biblioteca del admin: todas las fichas con su parte tecnica. La galeria muestra lo mismo sin ella. */
import { useState } from 'react'
import type { MediaResponse } from '../../api/client'
import { Icon } from '../../components/Icon'
import { AdminShell, ServerAlert } from './admin'
import { CacheUsageCard } from './CacheUsageCard'
import { PAGE, useAdminLibrary } from './hooks/useAdminLibrary'
import { LibraryTable } from './LibraryTable'
import { StatusDialog } from './StatusDialog'

export default function LibraryPage() {
  const [draft, setDraft] = useState('')
  const [q, setQ] = useState('')
  const [sort, setSort] = useState<'title' | 'added'>('title')
  const [offset, setOffset] = useState(0)
  // La ficha del dialogo abierto. Montarlo por ficha es lo que arranca y corta su polling.
  const [status, setStatus] = useState<MediaResponse | null>(null)
  const { page, failure, loading } = useAdminLibrary(q, sort, offset)

  // Otra busqueda u otro orden es otra lista: se vuelve a la primera pagina en el mismo
  // evento, y no en un efecto, para no pedir una vez con el offset viejo.
  const search = (text: string) => { setQ(text); setOffset(0) }

  return (
    <AdminShell
      crumb="Biblioteca"
      heading="Biblioteca"
      lead="Lo que hay cargado, con el video que dictó ffprobe. El estado de cada pista está en «Ver estado»."
      library
    >
      <CacheUsageCard />
      <section className="card">
        {loading && <div className="progress" aria-hidden="true" />}
        {/* Se busca al confirmar y no al tipear: asi no hace falta debounce. */}
        <form className="card-head lib-tools" role="search" onSubmit={(event) => { event.preventDefault(); search(draft.trim()) }}>
          <input
            className="input"
            type="search"
            value={draft}
            onChange={(event) => {
              setDraft(event.target.value)
              // La cruz del buscador vacia el campo: eso tambien es volver a todo.
              if (event.target.value === '') search('')
            }}
            placeholder="Buscar por título y Enter…"
            aria-label="Buscar en la biblioteca"
          />
          <select className="input" value={sort} onChange={(event) => { setSort(event.target.value as 'title' | 'added'); setOffset(0) }} aria-label="Orden">
            <option value="title">Por título</option>
            <option value="added">Agregadas recientemente</option>
          </select>
        </form>
        <div className="card-body">
          {failure !== null && <ServerAlert failure={failure} />}
          {page !== null && (page.total === 0
            ? <p className="empty">{q ? `Nada con "${q}".` : 'Todavía no hay fichas.'} <a href="/new/"><Icon name="plus" />Agregar un título</a></p>
            : <LibraryTable items={page.items} onStatus={setStatus} />)}
        </div>
        {/* Los numeros salen de la respuesta y no del estado: mientras carga, describen lo que se ve. */}
        {page !== null && page.total > PAGE && (
          <nav className="card-foot" aria-label="Páginas">
            <span className="grow">{page.offset + 1}–{page.offset + page.items.length} de {page.total} fichas</span>
            <button className="btn btn-outlined" type="button" onClick={() => setOffset(offset - PAGE)} disabled={loading || offset === 0}>
              <Icon name="chev-left" />Anterior
            </button>
            <button className="btn btn-outlined" type="button" onClick={() => setOffset(offset + PAGE)} disabled={loading || offset + PAGE >= page.total}>
              Siguiente<Icon name="chev-right" />
            </button>
          </nav>
        )}
      </section>
      {status !== null && <StatusDialog key={status.id_media} media={status} onClose={() => setStatus(null)} />}
    </AdminShell>
  )
}
