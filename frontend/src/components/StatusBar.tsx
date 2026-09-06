interface Props {
  /** Que esta pasando: "Reproduciendo — Blade Runner", "Procesando — 42%". */
  state: string
  /** Resolucion y canales, ya formateados. "—" cuando todavia no se sabe. */
  format: string
  duration: string
}

/** Los tres campos hundidos del pie de la ventana. */
export function StatusBar({ state, format, duration }: Props) {
  return (
    <div className="statusbar" role="status" aria-live="polite">
      <div className="field grow">{state}</div>
      <div className="field fixed">{format}</div>
      <div className="field narrow">{duration}</div>
    </div>
  )
}
