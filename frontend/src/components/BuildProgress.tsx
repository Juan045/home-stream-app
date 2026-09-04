interface Props {
  /** 0 a 1, avance del build de video. null cuando solo se esta rebufereando. */
  progress: number | null
  label: string
}

export function BuildProgress({ progress, label }: Props) {
  return (
    <div className="overlay">
      <div className="building" role="status" aria-live="polite">
        <div className="eq" aria-hidden="true">
          <i />
          <i />
          <i />
          <i />
          <i />
        </div>
        <div className="label">
          {progress === null ? label : `${label} — ${Math.round(progress * 100)}%`}
        </div>
        {progress !== null && (
          <div className="bar">
            <span style={{ width: `${Math.round(progress * 100)}%` }} />
          </div>
        )}
      </div>
    </div>
  )
}
