import { Link } from 'react-router';
import { FileDown, Printer } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, buttonVariants, PageContainer, PageHeader, PageMessage } from '@medvya/ui';
import type { EncounterBanner } from '../domain/encounter';
import { notesSection, sectionLabelKey, vitalNameKey, type NoteRecord, type NoteSection, type NoteTemplate, type NoteView } from '../domain/note';
import { answeredItems, answerLabelKey, isStructured, itemLabelKey } from '../domain/structured';
import PaperPrintPage, { Ident, Masthead, Meta, PaperFooter, Row, Rows, Signature } from '../../prescription/components/PaperPrintPage';

type Props = {
  banner: EncounterBanner | null; note: NoteView | null; template: NoteTemplate | null; loading: boolean; failed: boolean;
  downloading: boolean; pdfProblem: 'unsupported' | 'failed' | null; onPrint: () => void; onPdf: () => void;
  consultPath: string;
};

/**
 * E-06 print view in the print family layout (MDV-P1-111): the signed note as labelled rows a screen reader reads in order (reflows at 200 %
 * zoom), then each addendum as its own row. Print uses the browser; the PDF is the server's stored copy with the identifiers on every page.
 * MDV-P2-049: the one h1 names the clinic, the signer with their licence sits at the end; what was recorded at signing (vitals, allergies,
 * diagnoses, medicines) follows the patient, from the signed version only, in the same words and order as the PDF.
 */
export default function NotePrintPage(p: Props) {
  const { t, formatDate, formatTime, formatCalendarDate } = useI18n();
  if (p.loading) return <PageContainer width="form"><PageHeader title={t('notePrint.title')} /><PageMessage kind="busy">{t('notePrint.loading')}</PageMessage></PageContainer>;
  if (p.failed || !p.note?.signature || !p.template || !p.banner)
    return <PageContainer width="form"><PageHeader title={t('notePrint.title')} /><PageMessage kind="error">{t('notePrint.loadFailed')}</PageMessage></PageContainer>;
  const { note, banner } = p;
  const signer = note.signature!;
  const date = (iso: string) => formatDate(new Date(iso), banner.timeZone), time = (iso: string) => formatTime(new Date(iso), banner.timeZone);
  const signed = `${date(signer.signedAt)} ${time(signer.signedAt)}`;
  const reg = signer.signerLicence ? t('paper.reg', { number: ltr(signer.signerLicence) }) : null;
  const toolbar = <>
    <div className="flex flex-wrap gap-3">
      <Button onClick={p.onPrint}><Printer aria-hidden className="size-4" />{t('notePrint.print')}</Button>
      <Button variant="outline" loading={p.downloading} onClick={p.onPdf}><FileDown aria-hidden className="size-4" />{t('notePrint.pdf')}</Button>
      <Link to={p.consultPath} className={buttonVariants({ variant: 'outline' })}>{t('notePrint.back')}</Link>
    </div>
    {p.pdfProblem && <p role="alert" className="font-bold text-danger-700">{t(p.pdfProblem === 'unsupported' ? 'notePrint.pdfUnsupported' : 'notePrint.pdfFailed')}</p>}
  </>;
  // shortcut: the note data has no clinic logo, address, phone or verify QR code; the server PDF is the stored copy (MDV-P1-111).
  return (
    <PaperPrintPage label={t('notePrint.title')} toolbar={toolbar}>
      <Masthead clinic={note.facilityName ?? t('notePrint.title')} doctor={signer.signerName ? { name: signer.signerName, lines: [reg] } : null} />
      <Meta title={t('notePrint.title')}>{t('paper.noteMeta', { version: signer.versionNo, when: signed })}</Meta>
      <Rows>
        <Row id="print-patient" label={t('paper.row.patient')}>
          <Ident name={banner.patient.fullName} sub={t('paper.ident', { sex: t(`patients.sex.${banner.patient.sexAtBirth}`), age: banner.patient.ageYears,
            mrn: ltr(banner.patient.mrn), dob: formatCalendarDate(banner.patient.dateOfBirth) })} />
        </Row>
        <Row id="print-at-signing" label={t('notePrint.atSigning')}><AtSigning record={note.atSigning ?? null} timeZone={banner.timeZone} /></Row>
        {[...p.template.sections, notesSection].filter(s => note.sections[s.id]).map(s => (
          <Row key={s.id} id={`print-${s.id}`} label={t(sectionLabelKey(s.id))}>
            {isStructured(s) ? <Answers section={s} text={note.sections[s.id]!} /> : <p className="whitespace-pre-wrap">{note.sections[s.id]}</p>}
          </Row>
        ))}
        {(note.addenda ?? []).map(a => (
          <Row key={a.versionNo} id={`print-addendum-${a.versionNo}`} label={t('noteHistory.addendum', { amends: String(a.amendsVersionNo) })}>
            <p className="text-ink-700">{t(a.signerName ? 'noteHistory.signed' : 'noteHistory.signedOther',
              { version: String(a.versionNo), name: a.signerName ?? '', date: date(a.signedAt), time: time(a.signedAt) })}</p>
            <p>{t('noteHistory.reason', { reason: a.reason })}</p>
            <p className="whitespace-pre-wrap">{a.text}</p>
          </Row>
        ))}
      </Rows>
      {signer.signerName && <Signature name={signer.signerName} lines={[reg, t('paper.signed', { when: signed })]} />}
      <PaperFooter><p className="font-bold text-ink-900">{t('notePrint.corrections')}</p></PaperFooter>
    </PaperPrintPage>
  );
}

/** A structured section's answered items: the print shows what was answered, never the empty choices. */
function Answers({ section, text }: { section: NoteSection; text: string }) {
  const { t } = useI18n();
  const answered = answeredItems(section, text);
  return answered.length === 0 ? <p className="text-ink-700">{t('structured.noneAnswered')}</p>
    : <ul className="list-disc ps-5">{answered.map(a => <li key={a.id}>{t(a.note ? 'noteTemplates.answerLineNote' : 'noteTemplates.answerLine',
      { item: t(itemLabelKey(a.id)), answer: t(answerLabelKey(a.answer)), note: a.note ?? '' })}</li>)}</ul>;
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
    }), after: record.vitals.some(v => v.code === 'Weight') ? undefined : t('notePrint.weightNotRecorded') },
    { id: 'allergies', title: t('patient360.allergies'), empty: t(record.allergies.state === 'NoKnownAllergies' ? 'banner.noAllergies' : 'banner.allergiesNotRecorded'),
      lines: record.allergies.allergies.map(a => t(a.reaction ? 'notePrint.allergyReaction' : 'notePrint.allergy', {
        substance: a.substance, reaction: a.reaction ?? '', status: t(`patient360.allergyStatus.${a.status}`), source: t(`timeline.source.${a.source}`) })) },
    { id: 'diagnoses', title: t('problems.title'), empty: t('problems.empty'), lines: record.diagnoses.map(d => t('notePrint.diagnosis', {
      description: d.description, code: d.icd10Code ? ltr(d.icd10Code) : t('problems.uncoded'), status: t(`problems.status.${d.status}`), verification: t(`problems.verification.${d.verification}`), source: t(`timeline.source.${d.source}`) })) },
    { id: 'medicines', title: t('rxPrint.medicines'), empty: t('notePrint.medicinesEmpty'),
      lines: record.medicines.map(m => t('notePrint.medicine', { medicine: m.dose ? `${m.name} ${m.dose}` : m.name, number: ltr(m.number) })) },
  ];
  return (
    <div className="flex flex-col gap-[3pt]">
      {lists.map(l => (
        <section key={l.id} aria-labelledby={`print-${l.id}`}>
          <h3 id={`print-${l.id}`} className="font-bold">{l.title}</h3>
          {l.lines.length === 0 ? <p>{l.empty}</p> : <ul className="list-disc ps-5">{l.lines.map((line, i) => <li key={i}>{line}</li>)}</ul>}
          {'after' in l && l.after && <p>{l.after}</p>}
        </section>
      ))}
    </div>
  );
}
