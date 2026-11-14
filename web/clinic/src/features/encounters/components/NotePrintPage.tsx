import { Link } from 'react-router';
import { FileDown, Printer } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, buttonVariants, PageContainer, PageHeader, PageMessage } from '@medvya/ui';
import type { EncounterBanner } from '../domain/encounter';
import { sectionLabelKey, vitalNameKey, type NoteRecord, type NoteTemplate, type NoteView } from '../domain/note';
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
 * MDV-P2-049: the one h1 names the clinic, then the signer with their licence; what was recorded at signing (vitals, allergies, diagnoses,
 * medicines) follows the identifiers, from the signed version only, in the same words and order as the PDF.
 */
export default function NotePrintPage(p: Props) {
  const { t, formatCalendarDate } = useI18n();
  if (p.loading) return <PageContainer width="form"><PageHeader title={t('notePrint.title')} /><PageMessage kind="busy">{t('notePrint.loading')}</PageMessage></PageContainer>;
  if (p.failed || !p.note?.signature || !p.template || !p.banner)
    return <PageContainer width="form"><PageHeader title={t('notePrint.title')} /><PageMessage kind="error">{t('notePrint.loadFailed')}</PageMessage></PageContainer>;
  const { note, banner } = p;
  const signer = note.signature!;
  return (
    <div className="flex max-w-[720px] flex-col gap-4">
      <div className="flex flex-wrap gap-3 print:hidden">
        <Button onClick={p.onPrint}><Printer aria-hidden className="size-4" />{t('notePrint.print')}</Button>
        <Button variant="outline" loading={p.downloading} onClick={p.onPdf}><FileDown aria-hidden className="size-4" />{t('notePrint.pdf')}</Button>
        <Link to={p.consultPath} className={buttonVariants({ variant: 'outline' })}>{t('notePrint.back')}</Link>
      </div>
      {p.pdfProblem && <p role="alert" className="font-bold text-danger-700 print:hidden">{t(p.pdfProblem === 'unsupported' ? 'notePrint.pdfUnsupported' : 'notePrint.pdfFailed')}</p>}
      <article aria-label={t('notePrint.title')} className="flex flex-col gap-4 rounded-xl border bg-white p-6 text-black print:rounded-none print:border-0 print:p-0">
        {/* The header's h1 truncates to one line; on the print it wraps instead, so the clinic's name is never cut. */}
        <PageHeader title={<span className="whitespace-normal">{note.facilityName ? t('notePrint.heading', { facility: note.facilityName }) : t('notePrint.title')}</span>}
          className="border-b-2 border-black pb-2">
          {signer.signerName && <p className="font-bold">{signer.signerLicence ? t('notePrint.doctor', { name: signer.signerName, licence: ltr(signer.signerLicence) }) : signer.signerName}</p>}
          <p className="font-bold">{banner.patient.fullName}</p>
          <p className="flex flex-wrap gap-x-4">
            <span>{t('consult.banner.mrn', { mrn: ltr(banner.patient.mrn) })}</span>
            <span>{t('consult.banner.dob', { date: formatCalendarDate(banner.patient.dateOfBirth) })}</span>
          </p>
        </PageHeader>
        <AtSigning record={note.atSigning ?? null} timeZone={banner.timeZone} />
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

/** MDV-P2-049: the record kept at signing. Allergies in words: listed, no known allergies, or not recorded (never "none"). */
function AtSigning({ record, timeZone }: { record: NoteRecord | null; timeZone: string }) {
  const { t, formatDate, formatTime } = useI18n();
  if (!record) return <p className="font-bold">{t('notePrint.notCaptured')}</p>;
  const when = (iso: string) => ({ date: formatDate(new Date(iso), timeZone), time: formatTime(new Date(iso), timeZone) });
  const lists = [
    { id: 'vitals', title: t('noteAssist.vitals.title'), empty: t('notePrint.vitalsEmpty'), lines: record.vitals.map(v => {
      const line = t('noteAssist.vitalLine', { label: t(vitalNameKey(v.code)), value: String(v.value), unit: v.unit,
        source: v.recordedByName ? t('noteAssist.source', { name: v.recordedByName, ...when(v.takenAt) }) : t('noteAssist.sourceNoName', when(v.takenAt)) });
      return v.flag ? t('notePrint.flagged', { line, flag: t(`noteAssist.flag.${v.flag}`) }) : line;
    }) },
    { id: 'allergies', title: t('patient360.allergies'), empty: t(record.allergies.state === 'NoKnownAllergies' ? 'banner.noAllergies' : 'banner.allergiesNotRecorded'),
      lines: record.allergies.allergies.map(a => t(a.reaction ? 'notePrint.allergyReaction' : 'notePrint.allergy', {
        substance: a.substance, reaction: a.reaction ?? '', status: t(`patient360.allergyStatus.${a.status}`), source: t(`timeline.source.${a.source}`) })) },
    { id: 'diagnoses', title: t('problems.title'), empty: t('problems.empty'), lines: record.diagnoses.map(d => t('notePrint.diagnosis', {
      description: d.description, code: d.icd10Code ? ltr(d.icd10Code) : t('problems.uncoded'), verification: t(`problems.verification.${d.verification}`), source: t(`timeline.source.${d.source}`) })) },
    { id: 'medicines', title: t('rxPrint.medicines'), empty: t('notePrint.medicinesEmpty'),
      lines: record.medicines.map(m => t('notePrint.medicine', { medicine: m.dose ? `${m.name} ${m.dose}` : m.name })) },
  ];
  return (
    <section aria-labelledby="print-at-signing" className="flex flex-col gap-2">
      <h2 id="print-at-signing" className="font-extrabold">{t('notePrint.atSigning')}</h2>
      {lists.map(l => (
        <section key={l.id} aria-labelledby={`print-${l.id}`}>
          <h3 id={`print-${l.id}`} className="font-bold">{l.title}</h3>
          {l.lines.length === 0 ? <p>{l.empty}</p> : <ul className="list-disc ps-5">{l.lines.map((line, i) => <li key={i}>{line}</li>)}</ul>}
        </section>
      ))}
    </section>
  );
}
