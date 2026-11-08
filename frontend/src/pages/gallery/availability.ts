/**
 * Si una ficha se puede ver ya: el estado de `GET /media/{id}/encode` en textos
 * cortos para un disco. Sin React porque lo comparten el detalle y la ficha flotante.
 */
import type { EncodeResponse } from '../../api/client'

export interface Availability {
  big: string
  small: string
  /** La frase entera, para el lector de pantalla. */
  label: string
  tone: 'ready' | 'building' | 'failed' | 'neutral'
}

export function availability(encode: EncodeResponse | null): Availability {
  // Cargando o con error: el disco no dice nada que no se sepa.
  if (encode === null) return { big: '—', small: 'disponibilidad', label: 'Disponibilidad sin datos', tone: 'neutral' }
  switch (encode.state) {
    case 'ready':
      return { big: 'Lista', small: 'para ver', label: 'Lista para ver al instante', tone: 'ready' }
    case 'building': {
      // Sin avance esta esperando un slot de FFmpeg, como en el escenario.
      if (encode.progress === 0) return { big: 'En cola', small: 'preparando', label: 'En cola para prepararse', tone: 'building' }
      const percent = Math.round(encode.progress * 100)
      return { big: `${percent}%`, small: 'preparando', label: `Preparándose, ${percent} %`, tone: 'building' }
    }
    case 'failed':
      return { big: 'Falló', small: 'la preparación', label: 'Falló la preparación', tone: 'failed' }
    default:
      // idle y pending: el video se despacha con el primer Play.
      return { big: 'Play', small: 'se prepara al iniciar', label: 'Se prepara al darle Play', tone: 'neutral' }
  }
}
