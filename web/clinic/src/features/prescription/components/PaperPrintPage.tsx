import type { ReactNode } from 'react';
import { ShieldAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { PageHeader } from '@medvya/ui';
import { qrCells, type PaperSize } from '../domain/authorisation';

// Only while a paper is mounted (as the token slip does): the page size and the paper's margins, and a white page so nothing shades it.
const pageRule = { A4: '@page { size: A4; margin: 18mm 19mm 14mm; }', A5: '@page { size: A5; margin: 11mm 12mm 9mm; }' };
const whitePage = ' @media print { html, body, main { background: white !important; padding: 0 !important; } }';

// The paper's size on screen and the rhythm the design gives each size. The screen padding is the page margin; on paper @page gives it instead.
const sheet = {
  A4: `max-w-[210mm] md:px-[19mm] md:pt-[18mm] md:pb-[14mm]
    [--foot-qr:21mm] [--hang-gap:5mm] [--hang:27mm] [--items:3.4mm] [--meta-pb:5mm] [--row:2.6mm] [--sign-pt:6mm] [--sign-w:62mm]`,
  A5: `max-w-[148mm] md:px-[12mm] md:pt-[11mm] md:pb-[9mm]
    [--foot-qr:18mm] [--hang-gap:4mm] [--hang:21mm] [--items:2.2mm] [--meta-pb:3mm] [--row:1.7mm] [--sign-pt:2mm] [--sign-w:56mm]`,
};

type Props = { size?: PaperSize; label: string; toolbar: ReactNode; children: ReactNode; after?: ReactNode };

/**
 * The print family frame (MDV-P1-111, docs/02-design/print-family): one paper in white with the design's point and millimetre sizes, so the
 * screen preview is the paper at its real size and the browser prints it on A4 or A5. The controls above and anything after it never print.
 * A5 keeps the type size and tightens the label column, the rows and the footer, as the design does.
 */
export default function PaperPrintPage({ size = 'A4', label, toolbar, children, after }: Props) {
  return (
    <div className="flex flex-col gap-4">
      <style>{pageRule[size] + whitePage}</style>
      <div className="flex flex-col gap-3 print:hidden">{toolbar}</div>
      <article aria-label={label} data-paper={size}
        className={`flex w-full flex-col bg-white p-4 font-sans text-[10pt] leading-[1.38] text-ink-900 tabular-nums shadow-md [print-color-adjust:exact] ${sheet[size]}
          print:max-w-none print:p-0 print:shadow-none [&_p]:max-w-none`}>
        {children}
      </article>
      {after}
    </div>
  );
}

/** The doctor block at the masthead's end: name, then each line (qualifications, registration). */
export type PaperDoctor = { name: string; lines: (string | null | undefined)[] };

/** The clinic leads (the page's one h1, wrapping, never cut), its address under it; the doctor sits at the end edge; the teal rule under both. */
export function Masthead({ clinic, address, doctor }: { clinic: string; address?: string | null; doctor?: PaperDoctor | null }) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-x-[6mm] gap-y-[2mm] border-b-[2pt] border-brand-700 pb-[4.5mm]">
      <div className="min-w-0">
        <PageHeader title={clinic} className="[&_[data-slot=page-header-row]]:min-h-0 [&_h1]:whitespace-normal [&_h1]:text-[15pt] [&_h1]:leading-[1.15] [&_h1]:font-extrabold [&_h1]:tracking-[-0.01em] [&_h1]:text-brand-950" />
        {address && <p className="mt-[2pt] text-[7.6pt] text-ink-600">{address}</p>}
      </div>
      {doctor && (
        <div className="text-end">
          <p className="text-[11pt] font-extrabold">{doctor.name}</p>
          {doctor.lines.filter(Boolean).map(l => <p key={l} className="text-[7.6pt] text-ink-700">{l}</p>)}
        </div>
      )}
    </div>
  );
}

/** The paper's title in small teal capitals, and at the end its number, version and time. */
export function Meta({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="flex flex-wrap justify-between gap-x-[4mm] pt-[4pt] pb-(--meta-pb) text-[7.6pt] text-ink-600">
      <p className="font-extrabold tracking-[0.1em] text-brand-700 uppercase rtl:tracking-normal">{title}</p>
      {children && <p>{children}</p>}
    </div>
  );
}

/** The labelled rows: a hairline between rows, none above the first. */
export function Rows({ children }: { children: ReactNode }) {
  return <div className="flex flex-col [&>section:first-child]:border-t-0">{children}</div>;
}

/** One row: the label hangs in the start margin as a heading (read before its value), the value beside it. */
export function Row({ id, label, children }: { id: string; label: string; children: ReactNode }) {
  return (
    <section aria-labelledby={id} className="grid grid-cols-[var(--hang)_minmax(0,1fr)] gap-x-(--hang-gap) border-t-[0.5pt] border-line py-(--row)">
      <h2 id={id} className="pt-[2.2pt] text-[6.8pt] font-bold tracking-[0.08em] text-ink-600 uppercase rtl:tracking-normal">{label}</h2>
      <div className="min-w-0">{children}</div>
    </section>
  );
}

/** Who the paper is for: the name in bold, the identifiers under it. */
export function Ident({ name, sub }: { name: string; sub: string }) {
  return <><p className="text-[11.5pt] font-extrabold">{name}</p><p className="text-ink-700">{sub}</p></>;
}

/** A listed allergy: a red block with a shield and the word "Allergy", so it never rests on colour alone. */
export function Allergy({ children }: { children: ReactNode }) {
  const { t } = useI18n();
  return (
    <p className="inline-flex items-center gap-[1.8mm] rounded-[3pt] border-[0.8pt] border-danger-200 bg-danger-50 px-[7pt] py-[3.5pt] font-extrabold text-danger-700">
      <ShieldAlert aria-hidden className="size-[11pt] shrink-0" />
      <span>{t('paper.allergy')} <span className="font-bold text-ink-900">{children}</span></span>
    </p>
  );
}

/** A numbered list: medicines or tests. */
export function Items({ children }: { children: ReactNode }) {
  return <ol className="grid gap-(--items)">{children}</ol>;
}

/** One numbered item: the name in bold (a medicine's generic name in capitals), the detail after it in lighter type, then its lines. */
export function Item({ n, name, detail, caps, children }: { n: number; name: string; detail?: string | null; caps?: boolean; children?: ReactNode }) {
  return (
    <li className="grid grid-cols-[14pt_minmax(0,1fr)]">
      <span className="font-extrabold text-brand-700">{n}</span>
      <div>
        <p className="text-[11pt] font-extrabold tracking-[0.01em]">
          <span className={caps ? 'uppercase' : undefined}>{name}</span>
          {detail && <>{' '}<span className="text-[9.5pt] font-semibold tracking-normal text-ink-700">{detail}</span></>}
        </p>
        {children}
      </div>
    </li>
  );
}

/** A regulatory note in a thin box under its own medicine. */
export function Warning({ children }: { children: ReactNode }) {
  return <p className="mt-[2.5pt] rounded-[2.5pt] border-[0.6pt] border-ink-200 px-[6pt] py-[3.5pt] text-[7.2pt] leading-[1.35] text-ink-700">{children}</p>;
}

/** The signature box at the content's end edge: the space and line to sign (or, when given, the line saying no signature is needed), the name, then the registration and time. */
export function Signature({ name, lines, noSignatureNeeded }: { name: string; lines: (string | null | undefined)[]; noSignatureNeeded?: string }) {
  return (
    <div className="mt-auto flex justify-end pt-(--sign-pt) break-inside-avoid">
      <div className="w-(--sign-w) max-w-full">
        {noSignatureNeeded ? <p className="flex min-h-[11mm] items-end pb-[3pt] text-[7.6pt] text-ink-700">{noSignatureNeeded}</p> : <div className="h-[11mm]" />}
        <div className="border-t-[0.7pt] border-ink-900 pt-[3pt]">
          <p className="font-extrabold">{name}</p>
          {lines.filter(Boolean).map(l => <p key={l} className="text-[7.6pt] text-ink-700">{l}</p>)}
        </div>
      </div>
    </div>
  );
}

/** The one footer: the verify QR code when the paper has one, what it is for, and the number at the end. */
export function PaperFooter({ qr, qrLabel, title, children, end }: { qr?: string[]; qrLabel?: string; title?: string; children?: ReactNode; end?: ReactNode }) {
  return (
    <footer className="mt-[6mm] flex flex-wrap items-center gap-[4mm] border-t-[0.5pt] border-line pt-[3.5mm] text-[7.4pt] text-ink-600 break-inside-avoid">
      {qr && <QrCode rows={qr} label={qrLabel ?? ''} />}
      <div className="min-w-0 flex-1">
        {title && <p className="text-[8pt] font-bold text-ink-900">{title}</p>}
        {children}
      </div>
      {end && <p className="text-end whitespace-nowrap">{end}</p>}
    </footer>
  );
}

/** A link or code that reads left to right in mono, in any language. */
export function Mono({ children, wrap }: { children: string; wrap?: boolean }) {
  return <bdi dir="ltr" className={`font-mono text-ink-900 ${wrap ? 'break-all' : ''}`}>{children}</bdi>;
}

/** The QR code from the server's rows ('1' dark), quiet zone included, one square per module. */
function QrCode({ rows, label }: { rows: string[]; label: string }) {
  const size = rows.length;
  return (
    <svg role="img" aria-label={label} viewBox={`0 0 ${size} ${size}`} className="h-auto w-(--foot-qr) shrink-0" shapeRendering="crispEdges">
      <rect width={size} height={size} className="fill-white" />
      <g className="fill-black">{qrCells(rows).map(([x, y]) => <rect key={`${x}-${y}`} x={x} y={y} width={1} height={1} />)}</g>
    </svg>
  );
}
