/** El marcado que comparten el alta (/new/) y la edicion (/edit/). */
import { Fragment, type ReactNode } from 'react'
import type { MediaResponse } from '../../api/client'
import { Icon } from '../../components/Icon'
import { KINDS, MISSING } from '../../format'
import { channels, lang, videoLine, type Failure } from './media'
import './add.css'

/** Barra superior, riel y encabezado de la pagina. */
export function AdminShell(props: { crumb: string; heading: string; lead: string; adding?: boolean; children: ReactNode }) {
  return (
    <>
      <header className="appbar">
        {/* Un <a> y no un Link: cada vista es una pagina propia y no hay router. */}
        <a className="back sl" href="/"><Icon name="chev-left" />Cartelera</a>
        <div className="brand">
          <span className="brand-mark"><Icon name="mask" /></span>
          <span className="brand-name">Telón<span>.</span></span>
          <span className="badge">Admin</span>
        </div>
      </header>

      <div className="shell">
        {/* Descargas y Codificacion son solo una idea: no hay backend ni requerimientos.
            La edicion no tiene item: se entra por URL o desde una ficha. */}
        <nav className="rail" aria-label="Admin">
          <a href="/new/" aria-current={props.adding ? 'page' : undefined}><span className="pill sl"><Icon name="folder" /></span>Agregar</a>
          <a aria-disabled="true" title="Todavía no existe"><span className="pill"><Icon name="download" /></span>Descargas<small>Pronto</small></a>
          <a aria-disabled="true" title="Todavía no existe"><span className="pill"><Icon name="sliders" /></span>Codificación<small>Pronto</small></a>
        </nav>
        <div className="page">
          <div className="page-head">
            <div className="crumbs">Catálogo / <span>{props.crumb}</span></div>
            <h1>{props.heading}</h1>
            {props.lead && <p>{props.lead}</p>}
          </div>
          <main>{props.children}</main>
        </div>
      </div>
    </>
  )
}

/** La ficha como entrada de papel: lo que dicto ffprobe y lo editorial. */
export function Ticket({ media }: { media: MediaResponse }) {
  const audio = media.audio_tracks.filter((track) => !track.ignore)
  const subtitles = media.subtitle_tracks.filter((track) => !track.ignore)
  const facts: [string, string | null][] = [
    ['Video', `${videoLine(media)} · ${media.strategy === 'remux' ? 'sin recodificar' : 'transcodifica'}`],
    ['Audio', audio.length > 0 ? audio.map((t) => [lang(t.language), channels(t.channels)].filter(Boolean).join(' ')).join(' · ') : null],
    ['Subtítulos', subtitles.length > 0 ? subtitles.map((t) => lang(t.language) + (t.title ? ` ${t.title}` : '')).join(' · ') : null],
    ['Géneros', media.genres.length > 0 ? media.genres.join(', ') : null],
  ]
  const total = Math.round(media.duration / 60)
  const hours = Math.floor(total / 60)
  const minutes = total % 60

  return (
    <article className="ticket" aria-label={`Ficha de ${media.title}`}>
      <div className="ticket-main">
        <span className="kicker">{KINDS[media.kind] ?? media.kind}{media.year !== null && ` · ${media.year}`}</span>
        <h2>{media.title}</h2>
        <dl>
          {facts.map(([label, value]) => (
            <Fragment key={label}>
              <dt>{label}</dt>
              <dd className={value === null ? 'missing' : undefined}>{value ?? MISSING}</dd>
            </Fragment>
          ))}
        </dl>
        <p className={media.synopsis ? 'syn' : 'syn missing'}>{media.synopsis ?? 'Sin sinopsis todavía.'}</p>
      </div>
      <div className="ticket-stub">
        <span className={media.in_list ? 'stamp list' : 'stamp'}>{media.in_list ? 'Mi lista' : 'Admitido'}</span>
        <span className="time">{hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}` : minutes}</span>
        <span className="unit">{hours > 0 ? 'horas' : 'min'}</span>
        <span className="serial">{media.id_media.replace(/-/g, '').slice(-8).toUpperCase()}</span>
      </div>
    </article>
  )
}

export function FieldError({ id, msg, slug }: { id: string; msg: string; slug?: string | null }) {
  return (
    <p className="err" id={id} role="alert">
      <Icon name="alert" /><span>{msg}</span>{slug && <span className="code">{slug}</span>}
    </p>
  )
}

export function ServerAlert({ failure }: { failure: Failure }) {
  return (
    <div className="alert" role="alert">
      <Icon name="alert" />
      <div><p>{failure.msg}</p>{failure.slug && <span className="code">{failure.slug}</span>}</div>
    </div>
  )
}
