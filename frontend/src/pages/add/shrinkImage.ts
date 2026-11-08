/** Achica y comprime una imagen en el navegador antes de subirla, con APIs nativas. */
import type { ImageRole } from '../../api/client'

/** El tope del servidor (SaveMediaImageUseCase.MaxBytes). */
export const MAX_BYTES = 1_048_576

/** Medidas maximas: se escala sin recortar y sin agrandar. */
const MAX_SIZE: Record<ImageRole, [number, number]> = { poster: [600, 900], backdrop: [1920, 1080] }

/** WebP; si el navegador no lo codifica (Safari), JPEG. Rechaza si no se puede decodificar. */
export async function shrinkImage(file: Blob, role: ImageRole): Promise<Blob> {
  // from-image: la foto vertical de un celular viene acostada y con la rotacion en el EXIF.
  const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' })
  const [maxWidth, maxHeight] = MAX_SIZE[role]
  const scale = Math.min(1, maxWidth / bitmap.width, maxHeight / bitmap.height)
  const canvas = document.createElement('canvas')
  canvas.width = Math.max(1, Math.round(bitmap.width * scale))
  canvas.height = Math.max(1, Math.round(bitmap.height * scale))

  const ctx = canvas.getContext('2d')
  if (ctx === null) throw new Error('canvas sin contexto 2d')
  // El --bg de telon.css: un PNG transparente no queda negro en JPEG.
  ctx.fillStyle = '#061219'
  ctx.fillRect(0, 0, canvas.width, canvas.height)
  ctx.imageSmoothingQuality = 'high'
  ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
  bitmap.close()

  const webp = await toBlob(canvas, 'image/webp')
  // Sin soporte, toBlob devuelve PNG en vez de fallar.
  return webp.type === 'image/webp' ? webp : toBlob(canvas, 'image/jpeg')
}

function toBlob(canvas: HTMLCanvasElement, type: string): Promise<Blob> {
  return new Promise((resolve, reject) =>
    canvas.toBlob((blob) => (blob === null ? reject(new Error('toBlob fallo')) : resolve(blob)), type, 0.82),
  )
}
