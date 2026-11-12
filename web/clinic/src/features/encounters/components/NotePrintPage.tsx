import { Link } from 'react-router';
import { FileDown, Printer } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, buttonVariants } from '@medvya/ui';
import type { EncounterBanner } from '../domain/encounter';
import { sectionLabelKey, type NoteTemplate, type NoteView } from '../domain/note';
import { Versions } from './NoteHistory';
import { StructuredField } from './NoteParts';
import { isStructured } from '../domain/structured';

type Props = {
  banner: EncounterBanner | null; note: NoteView | null; template: NoteTemplate | null; loading: boolean; failed: boolean;
  downloading: boolean; pdfProblem: 'unsupported' | 'failed' | null; onPrint: () => void; onPdf: () => void;
  consultPath: string;
};

/**
 * E-06 print view: the signed note as plain headings and paragraphs (readable by a screen reader, reflows at 200 % zoom), then every addendum
 * in order, in black on white. Print uses the browser; the PDF is the server's stored copy with the identifiers on every page.
 */
export default function NotePrintPage(p: Props) {
  const { t, formatCalendarDate } = useI18n();
  if (p.loading) return <p role="status">{t('notePrint.loading')}</p>;
  if (p.failed || !p.note?.signature || !p.template || !p.banner) return <p role="alert" className="font-bold">{t('notePrint.loadFailed')}</p>;
  const { note, banner } = p;
  return (
    <div className="flex max-w-[720px] flex-col gap-4">
      <div className="flex flex-wrap gap-3 print:hidden">
        <Button className="min-h-11" onClick={p.onPrint}><Printer aria-hidden className="size-4" />{t('notePrint.print')}</Button>
        <Button variant="outline" className="min-h-11" loading={p.downloading} onClick={p.onPdf}><FileDown aria-hidden className="size-4" />{t('notePrint.pdf')}</Button>
        <Link to={p.consultPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('notePrint.back')}</Link>
      </div>
      {p.pdfProblem && <p role="alert" className="font-bold text-danger-700 print:hidden">{t(p.pdfProblem === 'unsupported' ? 'notePrint.pdfUnsupported' : 'notePrint.pdfFailed')}</p>}
      <article aria-labelledby="note-print-title" className="flex flex-col gap-4 rounded-xl border bg-white p-6 text-black print:rounded-none print:border-0 print:p-0">
        <header className="flex flex-col gap-1 border-b-2 border-black pb-2">
          <h1 id="note-print-title" className="text-xl font-extrabold">{t('notePrint.title')}</h1>
          <p className="font-bold">{banner.patient.fullName}</p>
          <p className="flex flex-wrap gap-x-4">
            <span>{t('consult.banner.mrn', { mrn: ltr(banner.patient.mrn) })}</span>
            <span>{t('consult.banner.dob', { date: formatCalendarDate(banner.patient.dateOfBirth) })}</span>
          </p>
        </header>
        {p.template.sections.filter(s => note.sections[s.id]).map(s => isStructured(s) ? (
          <section key={s.id}><StructuredField section={s} text={note.sections[s.id]!} readOnly heading onChange={() => undefined} /></section>
        ) : (
          <section key={s.id} aria-labelledby={`print-${s.id}`}>
            <h2 id={`print-${s.id}`} className="font-extrabold">{t(sectionLabelKey(s.id))}</h2>
            <p className="whitespace-pre-wrap">{note.sections[s.id]}</p>
          </section>
        ))}
        <section aria-labelledby="print-versions">
          <h2 id="print-versions" className="mb-1 font-extrabold">{t('noteHistory.title')}</h2>
          <Versions signature={p.note.signature} addenda={note.addenda ?? []} timeZone={banner.timeZone} />
        </section>
        <p className="border-t border-black pt-2 text-sm font-bold">{t('notePrint.corrections')}</p>
      </article>
    </div>
  );
}
