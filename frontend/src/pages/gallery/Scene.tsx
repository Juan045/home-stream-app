/**
 * La escena generada: el fondo por defecto de las fichas sin poster (hoy
 * todas, la API no tiene `poster_url`). Sale del id, asi la misma ficha
 * siempre se ve igual.
 */
import { useId } from 'react'

const PALETTES = [
  ['#2ad4e3', '#e5403a', '#ffc29f'], ['#ffc29f', '#1f7fa8', '#2ad4e3'], ['#ff7b4d', '#0e8a9c', '#ffc29f'],
  ['#6fb3a1', '#e5403a', '#ff7b4d'], ['#2ad4e3', '#b3262a', '#ffc29f'], ['#ffc29f', '#e5403a', '#ff7b4d'],
  ['#6fb3a1', '#1f7fa8', '#ffc29f'], ['#ff7b4d', '#b3262a', '#ffc29f'],
]

// FNV-1a: corto y estable entre recargas.
function hash(s: string) {
  let h = 2166136261
  for (const c of s) h = Math.imul(h ^ c.charCodeAt(0), 16777619)
  return h >>> 0
}

interface Props {
  media: { id_media: string; title: string; kind: string }
  /** Afiche 2:3: la misma escena recompuesta. Recortar la apaisada dejaba solo un borde. */
  portrait?: boolean
}

export function Scene({ media, portrait = false }: Props) {
  // Los gradientes van por id dentro del SVG: cada escena necesita los suyos.
  const uid = useId().replace(/[^\w-]/g, '')
  const h = hash(media.id_media + media.title)
  const [key, glow, rim] = PALETTES[h % PALETTES.length]
  const orb = [540 + ((h >> 3) % 140), 130 + ((h >> 7) % 60), 90 + ((h >> 11) % 50)]
  const figs0 = media.kind === 'series' ? [520, 610, 700] : media.kind === 'documentary' ? [640] : h & 1 ? [600] : [520, 660]
  const mid0 = (Math.min(...figs0) + Math.max(...figs0)) / 2

  const [W, H] = portrait ? [400, 600] : [800, 460]
  const xs = portrait ? figs0.map((x) => 200 + (x - mid0) * 0.75) : figs0
  const mid = portrait ? 200 : mid0
  const span = Math.max(...xs) - Math.min(...xs)
  const [ox, oy, or] = portrait ? [200 + (orb[0] - 610) * 0.9, 110 + (orb[1] - 130), orb[2] * 1.1] : orb
  const reach = portrait ? 100 : 120

  return (
    <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio={portrait ? 'xMidYMid slice' : 'xMaxYMax slice'} aria-hidden="true" focusable="false">
      <defs>
        <radialGradient id={`bg${uid}`} cx={portrait ? '50%' : '72%'} cy={portrait ? '22%' : '38%'} r="75%">
          <stop offset="0" stopColor={glow} stopOpacity=".5" />
          <stop offset=".5" stopColor={glow} stopOpacity=".1" />
          <stop offset="1" stopColor="#061219" stopOpacity="0" />
        </radialGradient>
        <radialGradient id={`orb${uid}`} cx="40%" cy="35%" r="70%">
          <stop offset="0" stopColor={rim} />
          <stop offset=".55" stopColor={glow} />
          <stop offset="1" stopColor={glow} stopOpacity="0" />
        </radialGradient>
        <linearGradient id={`beam${uid}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor={key} stopOpacity=".75" />
          <stop offset="1" stopColor={key} stopOpacity=".04" />
        </linearGradient>
        <radialGradient id={`floor${uid}`}>
          <stop offset="0" stopColor={key} stopOpacity=".55" />
          <stop offset="1" stopColor={key} stopOpacity="0" />
        </radialGradient>
        <filter id={`soft${uid}`} x="-20%" y="-20%" width="140%" height="140%">
          <feGaussianBlur stdDeviation="8" />
        </filter>
      </defs>
      <rect width={W} height={H} fill="#061219" />
      <rect width={W} height={H} fill={`url(#bg${uid})`} />
      <circle cx={ox} cy={oy} r={or} fill={`url(#orb${uid})`} opacity=".9" />
      <g filter={`url(#soft${uid})`} style={{ mixBlendMode: 'screen' }}>
        {xs.map((x, i) => (
          <polygon key={i} points={`${x - 16},-10 ${x + 16},-10 ${x + reach},${H - 10} ${x - reach},${H - 10}`} fill={`url(#beam${uid})`} />
        ))}
      </g>
      <ellipse cx={mid} cy={portrait ? 545 : 440} rx={(portrait ? 90 : 130) + span} ry="28" fill={`url(#floor${uid})`} />
      <g fill="#02070a" stroke={key} strokeOpacity=".55" strokeWidth="1.5">
        {xs.map((x, i) => (
          <g key={i} transform={portrait ? `translate(${x} -20) scale(1.2)` : `translate(${x} 0)`}>
            <circle cx="0" cy="285" r="21" />
            <path d="M-46 470 C-44 380 -38 334 -24 314 Q0 298 24 314 C38 334 44 380 46 470Z" />
          </g>
        ))}
      </g>
    </svg>
  )
}
