/** La tabla de la Biblioteca: una fila por ficha. Lo de cada pista va en el dialogo "Ver estado". */
import type { MediaResponse } from '../../api/client'
import { KINDS } from '../../format'
import { videoLine } from './media'

export function LibraryTable({ items, onStatus }: { items: MediaResponse[]; onStatus: (media: MediaResponse) => void }) {
  return (
    <div className="lib-scroll">
      <table className="lib-table">
        <thead>
          <tr>
            <th scope="col">Título</th>
            <th scope="col">Video</th>
            <th scope="col" aria-label="Acciones" />
          </tr>
        </thead>
        <tbody>
          {items.map((m) => (
            <tr key={m.id_media}>
              <th scope="row">
                {m.title}
                <small>{KINDS[m.kind] ?? m.kind}{m.year !== null && ` · ${m.year}`}</small>
                <code className="path">{m.file_path}</code>
              </th>
              <td>
                {videoLine(m)}
                <small>{m.strategy === 'remux' ? 'Remux' : 'Transcodifica'}</small>
              </td>
              <td className="actions">
                <button className="btn btn-outlined" type="button" onClick={() => onStatus(m)}>Ver estado</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
