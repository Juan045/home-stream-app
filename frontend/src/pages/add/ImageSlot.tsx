/** Una zona de imagen de /edit/: vista previa, elegir o soltar un archivo, y quitar. */
import { useEffect, useRef, useState, type DragEvent } from 'react'
import type { ImageRole } from '../../api/client'
import { Icon } from '../../components/Icon'
import { FieldError } from './admin'
import { MAX_BYTES, shrinkImage } from './shrinkImage'

/** Lo pendiente de guardar: una imagen nueva ya comprimida, o borrar la guardada. */
export type ImageChange = { blob: Blob; url: string } | 'remove'

// Mas que esto ni se intenta decodificar: trabaria la pestana.
const MAX_INPUT = 40 * 1024 * 1024

export function ImageSlot(props: {
  role: ImageRole
  title: string
  ratio: string
  savedUrl: string | null
  change: ImageChange | undefined
  disabled: boolean
  onChange: (change: ImageChange | undefined) => void
  // La pagina no deja guardar ni cancelar mientras una imagen se procesa.
  onBusy: (busy: boolean) => void
}) {
  const { role, change } = props
  const [over, setOver] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const locked = props.disabled || busy

  // La vista previa la crea este componente, y la libera al reemplazarla, descartarla o desmontar.
  useEffect(() => {
    if (change === undefined || change === 'remove') return
    return () => URL.revokeObjectURL(change.url)
  }, [change])

  async function take(file: File) {
    setError(null)
    if (!file.type.startsWith('image/')) return setError('Ese archivo no es una imagen.')
    if (file.size > MAX_INPUT) return setError('La imagen pesa más de 40 MB.')

    setBusy(true)
    props.onBusy(true)
    try {
      const blob = await shrinkImage(file, role)
      if (blob.size > MAX_BYTES) setError('Aún comprimida pesa más de 1 MB.')
      else props.onChange({ blob, url: URL.createObjectURL(blob) })
    } catch {
      setError('No se pudo leer la imagen.')
    } finally {
      setBusy(false)
      props.onBusy(false)
    }
  }

  function drop(event: DragEvent) {
    // Siempre: sin esto el navegador abre el archivo y se pierde la edicion.
    event.preventDefault()
    setOver(false)
    if (locked) return
    const file = event.dataTransfer.files.item(0)
    // Una imagen arrastrada desde otra pagina trae una URL, no un archivo.
    if (file === null) setError('Descargá la imagen y arrastrá el archivo.')
    else void take(file)
  }

  const src = change === 'remove' ? null : (change?.url ?? props.savedUrl)
  const errorId = error === null ? undefined : `${role}-err`

  return (
    <div className={`field slot ${role}`} role="group" aria-labelledby={`${role}-label`}>
      <span className="label" id={`${role}-label`}>{props.title}<span className="opt">{props.ratio}</span></span>
      <div
        className={over ? 'drop over' : 'drop'}
        onDragOver={(event) => {
          event.preventDefault()
          if (!locked) setOver(true)
        }}
        // Al pasar por encima de la imagen de adentro tambien salta: solo cuenta salir de la zona.
        onDragLeave={(event) => {
          if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setOver(false)
        }}
        onDrop={drop}
      >
        {busy
          ? <span className="hole">Procesando…</span>
          : src !== null
          ? <img src={src} alt={`Vista previa: ${props.title.toLowerCase()}`} />
          : <span className="hole">{change === 'remove' ? 'Se quita al guardar' : 'Sin imagen'}<small>Soltá un archivo acá</small></span>}
      </div>
      <div className="slot-actions">
        <button className="btn btn-outlined sl" type="button" onClick={() => inputRef.current?.click()} disabled={locked} aria-describedby={errorId}>
          <Icon name="folder" />Elegir imagen…
        </button>
        {change !== undefined && (
          <button className="btn btn-text sl" type="button" onClick={() => props.onChange(undefined)} disabled={locked}>
            <Icon name="replay" />Deshacer
          </button>
        )}
        {change === undefined && props.savedUrl !== null && (
          <button className="btn btn-text sl" type="button" onClick={() => props.onChange('remove')} disabled={locked}>
            <Icon name="x" />Quitar
          </button>
        )}
        <input
          ref={inputRef}
          type="file"
          accept="image/*"
          hidden
          onChange={(event) => {
            const file = event.target.files?.[0]
            if (file) void take(file)
            // Asi elegir el mismo archivo otra vez vuelve a disparar el change.
            event.target.value = ''
          }}
        />
      </div>
      <p className="desc" role="status">{busy ? 'Procesando…' : change === undefined ? '' : 'Pendiente: se guarda con la ficha.'}</p>
      {error !== null && <FieldError id={`${role}-err`} msg={error} />}
    </div>
  )
}
