export interface MenuItem {
  key: string
  label: string
  /** Columna derecha: canales, idioma, lo que distinga a la pista. */
  detail?: string
  checked?: boolean
  disabled?: boolean
  onSelect: () => void
}

interface Props {
  head: string
  items: MenuItem[]
  /** Debajo de la linea: acciones, no pistas. */
  extras?: MenuItem[]
}

function Row({ item }: { item: MenuItem }) {
  return (
    <button
      className="item"
      type="button"
      role="menuitemradio"
      aria-checked={item.checked ?? false}
      disabled={item.disabled}
      data-on={item.checked ?? false}
      onClick={item.onSelect}
    >
      <span className="tick" aria-hidden="true">
        {item.checked ? '✓' : ''}
      </span>
      <span className="label">{item.label}</span>
      {item.detail && <span className="detail">{item.detail}</span>}
    </button>
  )
}

/** Menu emergente de la barra inferior, anclado sobre su boton. */
export function TrackMenu({ head, items, extras }: Props) {
  return (
    <div className="menu" role="menu" aria-label={head}>
      <div className="head">{head}</div>
      {items.map((item) => (
        <Row key={item.key} item={item} />
      ))}
      {extras && extras.length > 0 && (
        <>
          <div className="rule" role="separator" />
          {extras.map((item) => (
            <Row key={item.key} item={item} />
          ))}
        </>
      )}
    </div>
  )
}
