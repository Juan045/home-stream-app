/** El avance de la codificacion. Sin avance ("en cola") la barra va indeterminada. */
export function Bar({ progress }: { progress: number }) {
  const percent = Math.round(progress * 100)
  const queued = progress === 0
  return (
    <div
      className={queued ? 'progress queued' : 'progress'}
      role="progressbar"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={queued ? undefined : percent}
      aria-label="Codificación"
    >
      <span style={{ width: `${percent}%` }} />
    </div>
  )
}
