/**
 * La barra de herramientas del catalogo.
 *
 * Solo tienen funcion los controles que `GET /media` sabe atender: la busqueda
 * (`q`, que es un LIKE sobre el titulo) y el orden (`sort`, que admite `title`
 * o `added`). "Genre" queda dibujado pero inerte: la API no tiene filtro por
 * genero — `_filters` en media_repository.py solo arma q, kind e in_list — y
 * filtrarlo en el cliente mentiria, porque el filtro se aplicaria a la pagina
 * y no al catalogo. El toggle List/Posters tampoco: la vista de lista no esta.
 */
export type Sort = 'title' | 'added'

interface Props {
  search: string
  onSearch: (value: string) => void
  sort: Sort
  onSort: (value: Sort) => void
}

const SORT_LABELS: Record<Sort, string> = {
  title: 'A–Z',
  added: 'Recently added',
}

export function LibraryToolbar({ search, onSearch, sort, onSort }: Props) {
  return (
    <div className="libbar">
      <div className="search">
        <i className="lens" aria-hidden="true" />
        <input
          type="search"
          value={search}
          onChange={(event) => onSearch(event.target.value)}
          // El q del backend es `title LIKE %q%`: solo titulos. Prometer gente
          // y generos seria mentir sobre lo que hace.
          placeholder="Search titles…"
          aria-label="Buscar en la biblioteca"
        />
      </div>

      {/* Dos opciones no justifican un desplegable: el boton las alterna y
          muestra la que esta puesta. */}
      <button
        className="btn"
        type="button"
        onClick={() => onSort(sort === 'title' ? 'added' : 'title')}
      >
        Sort: {SORT_LABELS[sort]} <span className="caret">▼</span>
      </button>

      <button className="btn" type="button" title="no available yet">
        Genre: All <span className="caret">▼</span>
      </button>

      <div className="sep" />

      <div className="toggle" role="group" aria-label="Vista">
        <button className="btn" type="button" title="no available yet">
          List
        </button>
        <button className="btn" type="button" data-on="true" aria-pressed="true">
          Posters
        </button>
      </div>

      <button className="btn user" type="button" title="no available yet">
        Juan <span className="caret">▼</span>
      </button>
    </div>
  )
}
