/**
 * La imagen de una ficha en la galeria: el poster en los lugares 2:3 y el
 * fondo en los apaisados, con la escena generada cuando falta.
 */
import { Scene } from './Scene'

interface Props {
  media: { id_media: string; title: string; kind: string; poster_url: string | null; backdrop_url: string | null }
  portrait?: boolean
}

export function Artwork({ media, portrait = false }: Props) {
  // Apaisado sin fondo: el poster recortado se parece mas a la ficha que la escena.
  const url = portrait ? media.poster_url : (media.backdrop_url ?? media.poster_url)
  // Decorativa: el titulo ya esta escrito al lado.
  if (url) return <img src={url} alt="" loading="lazy" decoding="async" />
  return <Scene media={media} portrait={portrait} />
}
