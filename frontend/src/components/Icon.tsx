// Importado y no en public/: Vite le pone hash al nombre, asi cada cambio del
// sprite es una URL nueva y ningun navegador se queda con el viejo en cache.
// no-inline: un sprite como data URI no sirve para <use href>.
import sprite from '../icons.svg?no-inline'

/** Un simbolo del sprite src/icons.svg. El tamano y el trazo los pone `.i` (telon.css). */
export function Icon({ name }: { name: string }) {
  return <svg className="i" aria-hidden="true"><use href={`${sprite}#${name}`} /></svg>
}
