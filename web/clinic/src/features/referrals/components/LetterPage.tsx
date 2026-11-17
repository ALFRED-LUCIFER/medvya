import { Link } from 'react-router';
import { FileDown, Printer } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, buttonVariants, PageHeader, PageMessage } from '@medvya/ui';
import type { Letter, LetterLine } from '../domain/referrals';
import PaperPrintPage, { Allergy, Ident, Masthead, Meta, PaperFooter, Row, Rows, Signature } from '../../prescription/components/PaperPrintPage';

type Props = { timeZone: string; letter: Letter | null; loading: boolean; notFound: boolean; failed: boolean; onPrint: () => void; consultPath: string;
  downloading: boolean; pdfProblem: 'unsupported' | 'failed' | null; onPdf: () => void };

/**
 * The referral letter (E-09) in the print family layout (MDV-P1-111): to whom and how urgent, who it is about, why, and only the chart sections
 * the doctor selected, each fact with where it came from. The app shell hides itself when printed. "Download PDF" gets the server's copy, stored with its
 * hash and a QR code that verifies it (MDV-P2-019, ADR-018).
 */
export default function LetterPage(p: Props) {
  const { t, formatDate, formatCalendarDate } = useI18n();
  if (p.loading || p.notFound || p.failed || !p.letter)
    return (
      <div className="flex max-w-[720px] flex-col gap-4">
        <PageHeader title={t('referrals.letter.title')} />
        {p.loading ? <PageMessage kind="busy">{t('referrals.letter.loading')}</PageMessage>
          : <PageMessage kind="error">{t(p.notFound ? 'referrals.letter.notFound' : 'referrals.letter.loadFailed')}</PageMessage>}
      </div>
    );
  const { letter } = p;
  const date = formatDate(new Date(letter.at), p.timeZone);
  const toolbar = <>
    <div className="flex flex-wrap gap-3">
      <Button onClick={p.onPrint}><Printer aria-hidden className="size-4" />{t('referrals.letter.print')}</Button>
      <Button variant="outline" loading={p.downloading} disabled={p.downloading} onClick={p.onPdf}><FileDown aria-hidden className="size-4" />{t('paper.pdf')}</Button>
      <Link to={p.consultPath} className={buttonVariants({ variant: 'outline' })}>{t('referrals.letter.back')}</Link>
    </div>
    {p.pdfProblem && <p role="alert" className="font-bold text-danger-700">{t(p.pdfProblem === 'unsupported' ? 'paper.pdfUnsupported' : 'paper.pdfFailed')}</p>}
  </>;
  const line = (l: LetterLine) => `${l.text} [${t(`timeline.source.${l.source}`)}]`; // each fact says where it came from
  // shortcut: the letter data has no clinic logo, address, phone, doctor registration or verify QR code; the server PDF carries the QR code (MDV-P1-111).
  return (
    <PaperPrintPage label={t('referrals.letter.title')} toolbar={toolbar}>
      <Masthead clinic={letter.facilityName} doctor={letter.doctorName ? { name: letter.doctorName, lines: [] } : null} />
      <Meta title={t('referrals.letter.title')}>{date}</Meta>
      <Rows>
        <Row id="letter-to" label={t('referrals.letter.to')}>
          <p><span className="font-bold">{letter.provider}</span><span className="text-ink-700">{` · ${t(`referrals.urgency.${letter.urgency}`)}`}</span></p>
        </Row>
        <Row id="letter-patient" label={t('paper.row.patient')}>
          <Ident name={letter.patient.fullName} sub={t('paper.identNoAge', { sex: t(`patients.sex.${letter.patient.sexAtBirth}`), mrn: ltr(letter.patient.mrn), dob: formatCalendarDate(letter.patient.dateOfBirth) })} />
        </Row>
        {letter.reason && <Row id="letter-reason" label={t('paper.row.reason')}><p>{letter.reason}</p></Row>}
        {letter.sections.map(s => (
          <Row key={s.section} id={`letter-${s.section}`} label={t(`referrals.section.${s.section}`)}>
            {s.lines.length === 0 ? <p>{t('referrals.letter.none')}</p>
              : s.section === 'Allergies' ? <Allergy>{s.lines.map(line).join('; ')}</Allergy>
              : <ul className="flex flex-col gap-[1pt]">{s.lines.map(l => <li key={`${l.text}-${l.source}`}>{l.text}<span className="ms-2 text-ink-700">{`[${t(`timeline.source.${l.source}`)}]`}</span></li>)}</ul>}
          </Row>
        ))}
      </Rows>
      {letter.doctorName && <Signature name={letter.doctorName} lines={[date]} />}
      <PaperFooter><p className="font-bold text-ink-900">{t('referrals.letter.notice')}</p></PaperFooter>
    </PaperPrintPage>
  );
}
