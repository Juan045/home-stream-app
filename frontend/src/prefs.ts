/**
 * Preferencias del espectador, guardadas en el navegador.
 *
 * Son ajustes de visualizacion, no estado del asset: el servidor no tiene por
 * que enterarse. localStorage puede tirar en modo privado o con las cookies
 * bloqueadas, asi que todo acceso va envuelto y cae a un default.
 */

const PREFIX = 'homeflix.'

export function readPref<T>(key: string, fallback: T, parse: (raw: string) => T): T {
  try {
    const raw = localStorage.getItem(PREFIX + key)
    return raw === null ? fallback : parse(raw)
  } catch {
    return fallback
  }
}

export function writePref(key: string, value: string): void {
  try {
    localStorage.setItem(PREFIX + key, value)
  } catch {
    // Sin storage la preferencia vale solo para esta sesion.
  }
}

export const readBool = (key: string, fallback = false): boolean =>
  readPref(key, fallback, (raw) => raw === '1')

export function readNumber(key: string, fallback: number, min: number, max: number): number {
  return readPref(key, fallback, (raw) => {
    const value = Number.parseFloat(raw)
    if (!Number.isFinite(value)) return fallback
    return Math.min(Math.max(value, min), max)
  })
}
