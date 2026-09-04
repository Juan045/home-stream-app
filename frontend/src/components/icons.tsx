/** Iconos del diseno, con las mismas proporciones que los artboards. */

export function PlayIcon({ size = 20, color = 'currentColor' }) {
  return (
    <svg width={size} height={size * 1.2} viewBox="0 0 20 24" aria-hidden="true">
      <path d="M0 0 L20 12 L0 24 Z" fill={color} />
    </svg>
  )
}

export function PauseIcon({ height = 15, color = 'currentColor' }) {
  return (
    <svg width={12} height={height} viewBox="0 0 12 15" aria-hidden="true">
      <rect x="0" y="0" width="4" height="15" rx="1" fill={color} />
      <rect x="8" y="0" width="4" height="15" rx="1" fill={color} />
    </svg>
  )
}

export function VolumeIcon({ muted = false }) {
  const fill = 'rgba(255,246,234,.8)'
  return (
    <svg width="13" height="14" viewBox="0 0 13 14" aria-hidden="true">
      <rect x="0" y="9" width="3" height="5" fill={fill} opacity={muted ? 0.3 : 1} />
      <rect x="5" y="5" width="3" height="9" fill={fill} opacity={muted ? 0.3 : 1} />
      <rect x="10" y="0" width="3" height="14" fill={fill} opacity={muted ? 0.3 : 1} />
      {muted && (
        <line x1="0" y1="14" x2="13" y2="0" stroke={fill} strokeWidth="1.5" />
      )}
    </svg>
  )
}

export function MenuIcon() {
  const fill = 'rgba(255,246,234,.8)'
  return (
    <svg width="14" height="12" viewBox="0 0 14 12" aria-hidden="true">
      <rect x="0" y="0" width="14" height="2" rx="1" fill={fill} />
      <rect x="0" y="5" width="14" height="2" rx="1" fill={fill} />
      <rect x="0" y="10" width="14" height="2" rx="1" fill={fill} />
    </svg>
  )
}

export function FullscreenIcon({ exit = false }) {
  const s = 'rgba(255,246,234,.85)'
  const w = 1.5
  return (
    <svg width="18" height="14" viewBox="0 0 18 14" aria-hidden="true" fill="none">
      {exit ? (
        <>
          <path d="M6 1 V5 H1" stroke={s} strokeWidth={w} />
          <path d="M12 1 V5 H17" stroke={s} strokeWidth={w} />
          <path d="M6 13 V9 H1" stroke={s} strokeWidth={w} />
          <path d="M12 13 V9 H17" stroke={s} strokeWidth={w} />
        </>
      ) : (
        <>
          <path d="M6 1 H1 V6" stroke={s} strokeWidth={w} />
          <path d="M12 1 H17 V6" stroke={s} strokeWidth={w} />
          <path d="M6 13 H1 V8" stroke={s} strokeWidth={w} />
          <path d="M12 13 H17 V8" stroke={s} strokeWidth={w} />
        </>
      )}
    </svg>
  )
}
