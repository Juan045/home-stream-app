/** Lo que comparten el dialogo "Ver estado" y sus filas: como se ve y se nombra cada estado. */
import type { ArtifactState, ArtifactStatus } from '../../api/client'

/** El estado tal como se pinta. "En cola" no es un estado del backend: es `building` con `queued`. */
export type Look = ArtifactState | 'queued'

export const look = (artifact: ArtifactStatus): Look => (artifact.queued ? 'queued' : artifact.state)

export const NAMES: Record<Look, string> = {
  idle: 'Sin procesar',
  pending: 'Pendiente',
  queued: 'En cola',
  building: 'Generando',
  ready: 'Listo',
  failed: 'Falló',
  interrupted: 'Interrumpido',
}

/** La leyenda, con el orden en que se lee. `idle` no tiene entrada: cuenta como pendiente. */
export const LEGEND: Look[] = ['ready', 'building', 'queued', 'pending', 'interrupted', 'failed']

export const legendKey = (artifact: ArtifactStatus): Look =>
  artifact.state === 'idle' ? 'pending' : look(artifact)
