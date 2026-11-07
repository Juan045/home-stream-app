import type { ReactNode } from 'react'
import { Icon } from '../../components/Icon'
import { Choice } from './admin'
import { codec } from './media'

/** La eleccion de codec de un rol: una opcion por cada codec que informa la API. */
export function CodecCard(props: {
  name: string
  icon: string
  title: string
  subtitle: string
  options: string[]
  value: string
  defaultValue: string
  // El texto de cada codec. Uno que todavia no tenga texto se ofrece igual, con su nombre.
  texts: Record<string, string>
  disabled: boolean
  onPick: (value: string) => void
  children?: ReactNode
}) {
  return (
    <section className="card">
      <div className="card-head">
        <span className="ico"><Icon name={props.icon} /></span>
        <div><h2>{props.title}</h2><p>{props.subtitle}</p></div>
      </div>
      <div className="card-body">
        <div className="choices two-up" role="radiogroup" aria-label={props.title}>
          {props.options.map((option) => (
            <Choice
              key={option}
              name={props.name}
              value={option}
              icon="chip"
              title={option === props.defaultValue ? `${codec(option)} · Predeterminado` : codec(option)}
              text={props.texts[option] ?? ''}
              checked={props.value === option}
              disabled={props.disabled}
              onPick={() => props.onPick(option)}
            />
          ))}
        </div>
        {props.children}
      </div>
    </section>
  )
}
