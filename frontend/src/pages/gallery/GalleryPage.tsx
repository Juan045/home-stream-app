/**
 * La cartelera (Telon): menu lateral, escenario con las ultimas agregadas y
 * secciones por tipo, con la ficha flotante al pasar sobre una tarjeta.
 *
 * Consume `GET /media` (una llamada por seccion), las ultimas agregadas para
 * el escenario, `GET /media/{id}` para la ficha y `PATCH /media/{id}` para
 * Mi lista. El estado AV1 sale solo en el escenario y en la ficha abierta.
 */
import { useEffect, useState, useSyncExternalStore, type ReactNode } from 'react'
import { ApiError, errorMessage, updateMedia, type MediaListItem, type MediaListResponse, type MediaResponse } from '../../api/client'
import { Icon } from '../../components/Icon'
import { KINDS, formatRuntime } from '../../format'
import { readBool, writePref } from '../../prefs'
import { useLibrary, type View } from './hooks/useLibrary'
import { usePeek, type Peek as PeekApi } from './hooks/usePeek'
import { useStage } from './hooks/useStage'
import { Peek } from './Peek'
import { Scene } from './Scene'
import { Stage } from './Stage'
import { playerUrl, reduceMotion } from './shared'
import './gallery.css'

/** Lo que se tarda en dejar de tipear antes de salir a buscar. */
const DEBOUNCE_MS = 300
const COLLAPSED_KEY = 'gallery.navCollapsed'

type Page = 'explore' | View

const NAV: { view: Page; label: string; icon: string }[] = [
  { view: 'explore', label: 'Explorar', icon: 'compass' },
  { view: 'list', label: 'Mi lista', icon: 'bookmark' },
  { view: 'film', label: 'Películas', icon: 'film' },
  { view: 'series', label: 'Series', icon: 'tv' },
  { view: 'documentary', label: 'Documentales', icon: 'leaf' },
]
const LABELS = Object.fromEntries(NAV.map((n) => [n.view, n.label])) as Record<Page, string>

/** Ventanas de M3: compacta (<600) el menu se abre encima; mediana (600-840) siempre rail. */
function useMatch(query: string) {
  return useSyncExternalStore(
    (onChange) => {
      const mq = matchMedia(query)
      mq.addEventListener('change', onChange)
      return () => mq.removeEventListener('change', onChange)
    },
    () => matchMedia(query).matches,
  )
}

export default function GalleryPage() {
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState<'title' | 'added'>('title')
  const [view, setView] = useState<Page>('explore')
  const [menuOpen, setMenuOpen] = useState(false)
  const [collapsed, setCollapsed] = useState(() => readBool(COLLAPSED_KEY))
  const [overrides, setOverrides] = useState<Record<string, MediaResponse>>({})
  const [saving, setSaving] = useState<string | null>(null)

  // Sin esto sale una request por tecla. El input responde igual: lo que se
  // demora es la busqueda, no lo que se ve escrito.
  useEffect(() => {
    const timer = setTimeout(() => setQuery(search.trim()), DEBOUNCE_MS)
    return () => clearTimeout(timer)
  }, [search])

  const library = useLibrary(query, sort)
  const stage = useStage()
  const peek = usePeek()
  const medium = useMatch('(min-width: 600px) and (max-width: 840px)')
  const compact = useMatch('(max-width: 599.98px)')
  // Arriba de 840 px se elige y se recuerda en el navegador.
  const rail = medium || (!compact && collapsed)

  const go = (next: Page) => {
    setView(next)
    setMenuOpen(false)
    peek.close()
    scrollTo({ top: 0 })
  }

  // PATCH /media/{id} { in_list }: el + del diseño. La respuesta es la ficha
  // entera, asi que pisa a la que tenian el escenario y la ficha flotante.
  const toggleList = (media: MediaResponse) => {
    setSaving(media.id_media)
    void updateMedia(media.id_media, { in_list: !media.in_list })
      .then((next) => {
        setOverrides((o) => ({ ...o, [next.id_media]: next }))
        library.reload()
      })
      .catch((err: unknown) => alert(errorMessage(err instanceof ApiError ? err.slug : 'unknown')))
      .finally(() => setSaving(null))
  }

  const ready = !library.loading && library.error === null
  const count = (v: Page) => (v === 'explore' ? library.total : (library.sections?.[v].total ?? 0))
  const listed = (v: Page) => (v === 'explore' ? library.hits : (library.sections?.[v] ?? null))

  const content = () => {
    if (library.loading) {
      return (
        <>
          <div className="skel stage-skel" />
          <div className="skel line" />
          <div className="posters">{Array.from({ length: 6 }, (_, i) => <div className="skel card" key={i} />)}</div>
        </>
      )
    }
    if (library.error !== null || library.sections === null) {
      return (
        <div className="notice error" role="alert">
          <span className="act">Función suspendida</span>
          <h2>{library.error}</h2>
        </div>
      )
    }
    if (library.total === 0 && !query) {
      return (
        <div className="notice">
          <span className="act">Sala vacía</span>
          <h2>Todavía no hay títulos en la cartelera.</h2>
          <p>Se agregan desde <a href="/new/"><code>/new/</code></a> con la ruta del archivo relativa a MEDIA_ROOT.</p>
        </div>
      )
    }

    if (query) {
      const hits = listed(view)
      if (!hits || hits.total === 0) {
        return (
          <div className="notice">
            <span className="act">Sin función</span>
            <h2>Nada con "{query}".</h2>
            <p>La búsqueda mira solo el título.</p>
          </div>
        )
      }
      return <Shelf act="Búsqueda" title={`Resultados para "${query}"`} page={hits} peek={peek} />
    }

    if (view !== 'explore') {
      return <Shelf act="Cartelera" title={LABELS[view]} page={library.sections[view]} tickets={view === 'list'} empty={emptyNote(view)} peek={peek} />
    }

    const sections = library.sections
    return (
      <>
        {stage === null ? (
          <div className="skel stage-skel" />
        ) : (
          stage.length > 0 && (
            <Stage items={stage.map((m) => overrides[m.id_media] ?? m)} peek={peek} saving={saving} onToggleList={toggleList} />
          )
        )}
        {(['list', 'film', 'series', 'documentary'] as const)
          .filter((v) => v === 'list' || sections[v].total > 0)
          .map((v) => (
            <Shelf key={v} title={LABELS[v]} page={sections[v]} tickets={v === 'list'} empty={emptyNote(v)} onAll={() => go(v)} peek={peek} />
          ))}
      </>
    )
  }

  return (
    <div className={rail ? 'app nav-rail' : 'app'}>
      <aside className={menuOpen ? 'sidebar open' : 'sidebar'} id="sidebar">
        <Brand>
          <button
            className="icon-btn collapse-btn"
            type="button"
            aria-controls="sidebar"
            aria-expanded={!rail}
            aria-label={rail ? 'Desplegar menú' : 'Plegar menú'}
            title={rail ? 'Desplegar menú' : 'Plegar menú'}
            onClick={() => {
              writePref(COLLAPSED_KEY, collapsed ? '0' : '1')
              setCollapsed(!collapsed)
            }}
          >
            <Icon name="panel" />
          </button>
        </Brand>
        <p className="nav-label">Cartelera</p>
        <nav className="nav" aria-label="Principal">
          {NAV.map((n) => (
            <button type="button" key={n.view} aria-current={view === n.view ? 'page' : undefined} onClick={() => go(n.view)}>
              <span className="ico"><Icon name={n.icon} /></span>
              {n.label}
              {/* Los totales que ya se piden: siguen a la busqueda. */}
              {ready && count(n.view) > 0 && <span className="count">{count(n.view)}</span>}
            </button>
          ))}
        </nav>
      </aside>
      <div className={menuOpen ? 'scrim show' : 'scrim'} onClick={() => setMenuOpen(false)} />
      <Peek peek={peek} overrides={overrides} saving={saving} onToggleList={toggleList} />

      <main className="main">
        <header className="topbar">
          <button className="icon-btn menu-btn" type="button" aria-label="Abrir menú" onClick={() => setMenuOpen(true)}><Icon name="menu" /></button>
          <label className="search">
            <Icon name="search" />
            <input
              type="search"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value)
                peek.close()
              }}
              // El q del backend es `title LIKE %q%`: solo titulos.
              placeholder="Buscar títulos…"
              aria-label="Buscar en la biblioteca"
            />
            {/* El boton de filtros del diseño es el orden: el unico otro filtro de la API. */}
            <button
              className="sort-btn"
              type="button"
              aria-label={`Orden: ${sort === 'title' ? 'alfabético' : 'agregados recientemente'}`}
              onClick={(e) => {
                e.preventDefault()
                setSort(sort === 'title' ? 'added' : 'title')
                peek.close()
              }}
            >
              <Icon name="sort" /><span className="lbl">{sort === 'title' ? 'A–Z' : 'Recientes'}</span>
            </button>
          </label>
          {ready && <span className="total">{titles(library.total)}{query && ` · "${query}"`}</span>}
        </header>

        <div className="content" aria-busy={library.loading}>{content()}</div>

        <footer className="site-foot">
          <div className="foot-bulbs" aria-hidden="true" />
          <div className="foot-top">
            <div className="foot-brand">
              <Brand />
              <p>Tu biblioteca, servida desde casa. Lo que agregás se puede ver mientras se prepara.</p>
            </div>
            <nav className="foot-col" aria-label="Cartelera">
              <h4>Cartelera</h4>
              <ul>{NAV.map((n) => <li key={n.view}><button type="button" onClick={() => go(n.view)}>{n.label}</button></li>)}</ul>
            </nav>
            <nav className="foot-col" aria-label="Admin">
              <h4>Admin</h4>
              <ul>
                <li><a href="/new/">Agregar un título</a></li>
                <li><span className="soon">Descargas <small>Pronto</small></span></li>
                <li><span className="soon">Codificación <small>Pronto</small></span></li>
              </ul>
            </nav>
            <button className="to-top" type="button" aria-label="Volver arriba" title="Volver arriba" onClick={() => scrollTo({ top: 0, behavior: reduceMotion ? 'auto' : 'smooth' })}>
              <Icon name="arrow" />
            </button>
          </div>
          {/* Sale de los GET /media que ya hace la pagina: no hay endpoint de salud del servidor. */}
          <div className="foot-status" aria-live="polite">
            {library.loading ? (
              <span className="item"><span className="dot" />Cargando la cartelera…</span>
            ) : library.error !== null ? (
              <span className="item"><span className="dot off" />{library.error}</span>
            ) : (
              <>
                <span className="item"><span className="dot" /><b>{library.total}</b> {library.total === 1 ? 'título' : 'títulos'} en cartelera</span>
                <span className="item"><b>{count('list')}</b> en Mi lista</span>
              </>
            )}
          </div>
          <div className="foot-giant" aria-hidden="true">Telón<span>.</span></div>
        </footer>
      </main>
    </div>
  )
}

function Brand({ children }: { children?: ReactNode }) {
  return (
    <div className="brand">
      <span className="brand-mark"><Icon name="mask" /></span>
      <div>
        <div className="brand-name">Telón<span>.</span></div>
        <span className="brand-sub">Teatro en casa</span>
      </div>
      {children}
    </div>
  )
}

const titles = (n: number) => `${n} ${n === 1 ? 'título' : 'títulos'}`
const emptyNote = (v: View) => (v === 'list' ? 'Todavía no agregaste nada. Usá el + de una ficha para guardarla acá.' : 'No hay títulos de este tipo.')

interface ShelfProps {
  act?: string
  title: string
  page: MediaListResponse
  /** Mi lista va en entradas de papel; el resto, en posters. */
  tickets?: boolean
  empty?: string
  /** Con "Ver todas" es un adelanto (4 entradas o 12 posters); sin, la seccion entera. */
  onAll?: () => void
  peek: PeekApi
}

function Shelf({ act, title, page, tickets = false, empty, onAll, peek }: ShelfProps) {
  const shown = onAll ? page.items.slice(0, tickets ? 4 : 12) : page.items
  return (
    <section className="section">
      <div className="section-head">
        <div>
          {act && <span className="act">{act}</span>}
          <h2>{title}</h2>
        </div>
        <div className="rule" />
        {onAll ? (
          <button className="see-all" type="button" onClick={onAll}>Ver todas <Icon name="arrow" /></button>
        ) : (
          <span className="section-count">{titles(page.total)}</span>
        )}
      </div>
      {page.items.length === 0 ? (
        <p className="soft-note">{empty}</p>
      ) : tickets ? (
        <div className="tickets">{shown.map((m) => <Ticket media={m} peek={peek} key={m.id_media} />)}</div>
      ) : (
        <div className="posters">{shown.map((m) => <Poster media={m} peek={peek} key={m.id_media} />)}</div>
      )}
    </section>
  )
}

/** Mi lista: entrada de papel con la duracion en el talon. */
function Ticket({ media, peek }: { media: MediaListItem; peek: PeekApi }) {
  const total = Math.round(media.duration / 60)
  const hours = Math.floor(total / 60)
  const minutes = total % 60
  const kind = KINDS[media.kind] ?? media.kind
  return (
    <a className="ticket" href={playerUrl(media.id_media)} {...peek.card(media.id_media)}>
      <div className="ticket-main">
        {/* Sin etiqueta AV1: el estado seria un pedido por titulo. Va el tipo. */}
        <div className="ticket-top"><span className="mini"><Scene media={media} /></span><span className="tag">{kind}</span></div>
        <div>
          <h3>{media.title}</h3>
          {/* Solo el ano: el tipo ya esta en la etiqueta y el listado no trae generos. */}
          {media.year !== null && <p>{media.year}</p>}
        </div>
      </div>
      <div className="ticket-stub">
        <span className="time">{hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}` : minutes}</span>
        <span className="unit">{hours > 0 ? 'horas' : 'min'}</span>
        <span className="serial">{media.id_media.replace(/-/g, '').slice(-7).toUpperCase()}</span>
      </div>
    </a>
  )
}

/** Afiche 2:3 con la escena vertical. */
function Poster({ media, peek }: { media: MediaListItem; peek: PeekApi }) {
  return (
    <a className="poster" href={playerUrl(media.id_media)} aria-label={`Ver ${media.title}`} {...peek.card(media.id_media)}>
      <div className="art"><Scene media={media} portrait /></div>
      <span className="play"><Icon name="play" /></span>
      <div className="poster-info">
        <h3>{media.title}</h3>
        <p>{media.year !== null && <><b>{media.year}</b> · </>}{formatRuntime(media.duration)}</p>
      </div>
    </a>
  )
}
