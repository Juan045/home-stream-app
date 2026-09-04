interface Props {
  title: string
  message: string
  onRetry?: () => void
}

export function PlaybackError({ title, message, onRetry }: Props) {
  return (
    <div className="overlay">
      <div className="failure" role="alert">
        <div className="rule" />
        <h1>{title}</h1>
        <p>{message}</p>
        <div className="actions">
          {onRetry && (
            <button className="btn-primary" type="button" onClick={onRetry}>
              Reintentar
            </button>
          )}
          <button
            className="btn-quiet"
            type="button"
            onClick={() => location.reload()}
          >
            Recargar la pagina
          </button>
        </div>
      </div>
    </div>
  )
}
