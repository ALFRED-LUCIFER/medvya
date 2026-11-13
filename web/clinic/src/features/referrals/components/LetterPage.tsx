import { Link } from 'react-router';
import { FileDown, Printer } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, PageHeader, PageMessage } from '@medvya/ui';
import type { Letter } from '../domain/referrals';

type Props = { letter: Letter | null; loading: boolean; notFound: boolean; failed: boolean; onPrint: () => void; consultPath: string;
  downloading: boolean; pdfProblem: 'unsupported' | 'failed' | null; onPdf: () => void };

/**
 * The referral letter (E-09): who it is about and from, to whom, why and urgency, and only the chart sections the doctor selected, each fact
 * with where it came from. Plain black on white; the app shell hides itself when printed. "Download PDF" gets the server's copy, stored with its
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
  return (
    <div className="flex max-w-[720px] flex-col gap-4">
      <div className="flex flex-wrap gap-3 print:hidden">
        <Button onClick={p.onPrint}><Printer aria-hidden className="size-4" />{t('referrals.letter.print')}</Button>
        <Button variant="outline" loading={p.downloading} disabled={p.downloading} onClick={p.onPdf}><FileDown aria-hidden className="size-4" />{t('paper.pdf')}</Button>
        <Link to={p.consultPath} className={buttonVariants({ variant: 'outline' })}>{t('referrals.letter.back')}</Link>
      </div>
      {p.pdfProblem && <p role="alert" className="font-bold text-danger-700 print:hidden">{t(p.pdfProblem === 'unsupported' ? 'paper.pdfUnsupported' : 'paper.pdfFailed')}</p>}
      <article aria-label={t('referrals.letter.title')} className="flex flex-col gap-4 rounded-xl border bg-white p-6 text-black print:rounded-none print:border-0 print:p-0">
        {/* On paper the title wraps; it is never cut. */}
        <PageHeader className="border-b-2 border-black pb-2 [&_h1]:whitespace-normal" title={t('referrals.letter.title')}>
          <p className="font-bold">{letter.facilityName}</p>
        </PageHeader>
        <dl className="grid grid-cols-[max-content_1fr] gap-x-6 gap-y-1">
          <dt className="font-bold">{t('referrals.letter.to')}</dt><dd>{letter.provider}</dd>
          <dt className="font-bold">{t('referrals.letter.urgency')}</dt><dd>{t(`referrals.urgency.${letter.urgency}`)}</dd>
          <dt className="font-bold">{t('referrals.letter.patient')}</dt><dd>{letter.patient.fullName}</dd>
          <dt className="font-bold">{t('referrals.letter.mrn')}</dt><dd><bdi dir="ltr">{letter.patient.mrn}</bdi></dd>
          <dt className="font-bold">{t('referrals.letter.dob')}</dt><dd>{formatCalendarDate(letter.patient.dateOfBirth)}</dd>
          <dt className="font-bold">{t('referrals.letter.sex')}</dt><dd>{t(`patients.sex.${letter.patient.sexAtBirth}`)}</dd>
          {letter.doctorName && <><dt className="font-bold">{t('referrals.letter.from')}</dt><dd>{letter.doctorName}</dd></>}
          <dt className="font-bold">{t('referrals.letter.date')}</dt><dd>{formatDate(new Date(letter.at))}</dd>
        </dl>
        {letter.reason && <section aria-labelledby="letter-reason"><h2 id="letter-reason" className="mb-1 font-extrabold">{t('referrals.letter.reason')}</h2><p>{letter.reason}</p></section>}
        {letter.sections.map(s => (
          <section key={s.section} aria-labelledby={`letter-${s.section}`}>
            <h2 id={`letter-${s.section}`} className="mb-1 font-extrabold">{t(`referrals.section.${s.section}`)}</h2>
            {s.lines.length === 0
              ? <p>{t('referrals.letter.none')}</p>
              : <ul className="flex flex-col gap-1">{s.lines.map(l => <li key={`${l.text}-${l.source}`}>{l.text}<span className="ms-2 text-sm">{`[${t(`timeline.source.${l.source}`)}]`}</span></li>)}</ul>}
          </section>
        ))}
        <p className="border-t border-black pt-2 text-sm font-bold">{t('referrals.letter.notice')}</p>
      </article>
    </div>
  );
}
