/**
 * La barra de herramientas del catalogo.
 *
 * Los controles estan dibujados pero no conectados: la busqueda, el orden y el
 * filtro por genero necesitan `GET /media` y esta vista todavia no habla con
 * el backend. El toggle marca "Posters" porque la vista de lista (artboard 1a)
 * no esta hecha.
 */
export function LibraryToolbar() {
  return (
    <div className="libbar">
      <div className="search">
        <i className="lens" aria-hidden="true" />
        <input
          type="search"
          readOnly
          placeholder="Search titles, people, genres…"
          aria-label="Buscar en la biblioteca"
          title="Todavia no conectado"
        />
      </div>

      <button className="btn" type="button">
        Sort: A–Z <span className="caret">▼</span>
      </button>
      <button className="btn" type="button">
        Genre: All <span className="caret">▼</span>
      </button>

      <div className="sep" />

      <div className="toggle" role="group" aria-label="Vista">
        <button className="btn" type="button">
          List
        </button>
        <button className="btn" type="button" data-on="true" aria-pressed="true">
          Posters
        </button>
      </div>

      <button className="btn user" type="button">
        Juan <span className="caret">▼</span>
      </button>
    </div>
  )
}
