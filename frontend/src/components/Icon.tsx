/** Un simbolo del sprite public/icons.svg. El tamano y el trazo los pone `.i` (telon.css). */
export function Icon({ name }: { name: string }) {
  return <svg className="i" aria-hidden="true"><use href={`/icons.svg#${name}`} /></svg>
}
