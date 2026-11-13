import { Link } from 'react-router';
import { FileDown, Printer } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, PageHeader, PageMessage } from '@medvya/ui';
import type { OrderSlip } from '../domain/orders';

type Props = { slip: OrderSlip | null; loading: boolean; nothingPlaced: boolean; failed: boolean; onPrint: () => void; consultPath: string;
  downloading: boolean; pdfProblem: 'unsupported' | 'failed' | null; onPdf: () => void };

/**
 * The investigation slip (E-05): who it is for, who ordered, where it goes and each test with its instructions, in plain black on white.
 * The page prints (the app shell hides itself); "Download PDF" gets the server's copy, stored with its hash and a QR code that verifies it
 * (MDV-P2-018, ADR-018).
 */
export default function SlipPage(p: Props) {
  const { t, formatDate, formatCalendarDate } = useI18n();
  if (p.loading || p.nothingPlaced || p.failed || !p.slip)
    return (
      <div className="flex max-w-[720px] flex-col gap-4">
        <PageHeader title={t('orders.slip.title')} />
        {p.loading ? <PageMessage kind="busy">{t('orders.slip.loading')}</PageMessage>
          : <PageMessage kind="error">{t(p.nothingPlaced ? 'orders.slip.notFound' : 'orders.slip.loadFailed')}</PageMessage>}
      </div>
    );
  const { slip } = p;
  return (
    <div className="flex max-w-[720px] flex-col gap-4">
      <div className="flex flex-wrap gap-3 print:hidden">
        <Button onClick={p.onPrint}><Printer aria-hidden className="size-4" />{t('orders.slip.print')}</Button>
        <Button variant="outline" loading={p.downloading} disabled={p.downloading} onClick={p.onPdf}><FileDown aria-hidden className="size-4" />{t('paper.pdf')}</Button>
        <Link to={p.consultPath} className={buttonVariants({ variant: 'outline' })}>{t('orders.slip.back')}</Link>
      </div>
      {p.pdfProblem && <p role="alert" className="font-bold text-danger-700 print:hidden">{t(p.pdfProblem === 'unsupported' ? 'paper.pdfUnsupported' : 'paper.pdfFailed')}</p>}
      <article aria-label={t('orders.slip.title')} className="flex flex-col gap-4 rounded-xl border bg-white p-6 text-black print:rounded-none print:border-0 print:p-0">
        {/* On paper the title wraps; it is never cut. */}
        <PageHeader className="border-b-2 border-black pb-2 [&_h1]:whitespace-normal" title={t('orders.slip.title')}>
          <p className="font-bold">{slip.facilityName}</p>
        </PageHeader>
        <dl className="grid grid-cols-[max-content_1fr] gap-x-6 gap-y-1">
          <dt className="font-bold">{t('orders.slip.patient')}</dt><dd>{slip.patient.fullName}</dd>
          <dt className="font-bold">{t('orders.slip.mrn')}</dt><dd><bdi dir="ltr">{slip.patient.mrn}</bdi></dd>
          <dt className="font-bold">{t('orders.slip.dob')}</dt><dd>{formatCalendarDate(slip.patient.dateOfBirth)}</dd>
          <dt className="font-bold">{t('orders.slip.sex')}</dt><dd>{t(`patients.sex.${slip.patient.sexAtBirth}`)}</dd>
          {slip.doctorName && <><dt className="font-bold">{t('orders.slip.doctor')}</dt><dd>{slip.doctorName}</dd></>}
          <dt className="font-bold">{t('orders.slip.date')}</dt><dd>{formatDate(new Date(slip.orderedAt))}</dd>
          {slip.destination && <><dt className="font-bold">{t('orders.slip.lab')}</dt><dd>{slip.destination}</dd></>}
        </dl>
        <section aria-labelledby="slip-tests">
          <h2 id="slip-tests" className="mb-1 font-extrabold">{t('orders.slip.tests')}</h2>
          <ul className="flex flex-col gap-1">
            {slip.orders.map(o => (
              <li key={o.id} className="border-b border-black/40 pb-1">
                <span className="font-bold">{o.name}</span> <span>{`(${t(`orders.kind.${o.kind}`)})`}</span>
                {o.note && <span className="block">{o.note}</span>}
              </li>
            ))}
          </ul>
        </section>
        <p className="border-t border-black pt-2 text-sm font-bold">{t('orders.slip.notice')}</p>
      </article>
    </div>
  );
}
