import { Link } from 'react-router';
import { FileDown, Printer } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, buttonVariants, PageHeader, PageMessage } from '@medvya/ui';
import type { OrderSlip } from '../domain/orders';
import PaperPrintPage, { Ident, Item, Items, Masthead, Meta, Row, Rows, Signature } from '../../prescription/components/PaperPrintPage';

type Props = { timeZone: string; slip: OrderSlip | null; loading: boolean; nothingPlaced: boolean; failed: boolean; onPrint: () => void; consultPath: string;
  downloading: boolean; pdfProblem: 'unsupported' | 'failed' | null; onPdf: () => void };

/**
 * The investigation slip (E-05) in the print family layout (MDV-P1-111): who it is for, who ordered, where it goes and each test, numbered, with
 * its instructions. The page prints (the app shell hides itself); "Download PDF" gets the server's copy, stored with its hash and a QR code that verifies it
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
  const ordered = t('paper.ordered', { when: formatDate(new Date(slip.orderedAt), p.timeZone) });
  const toolbar = <>
    <div className="flex flex-wrap gap-3">
      <Button onClick={p.onPrint}><Printer aria-hidden className="size-4" />{t('orders.slip.print')}</Button>
      <Button variant="outline" loading={p.downloading} disabled={p.downloading} onClick={p.onPdf}><FileDown aria-hidden className="size-4" />{t('paper.pdf')}</Button>
      <Link to={p.consultPath} className={buttonVariants({ variant: 'outline' })}>{t('orders.slip.back')}</Link>
    </div>
    {p.pdfProblem && <p role="alert" className="font-bold text-danger-700">{t(p.pdfProblem === 'unsupported' ? 'paper.pdfUnsupported' : 'paper.pdfFailed')}</p>}
  </>;
  // shortcut: the slip data has no clinic logo, address, phone, doctor registration or verify QR code; the server PDF carries the QR code (MDV-P1-111).
  return (
    <PaperPrintPage label={t('orders.slip.title')} toolbar={toolbar}>
      <Masthead clinic={slip.facilityName} doctor={slip.doctorName ? { name: slip.doctorName, lines: [] } : null} />
      <Meta title={t('orders.slip.title')}>{ordered}</Meta>
      <Rows>
        <Row id="slip-patient" label={t('paper.row.patient')}>
          <Ident name={slip.patient.fullName} sub={t('paper.identNoAge', { sex: t(`patients.sex.${slip.patient.sexAtBirth}`), mrn: ltr(slip.patient.mrn), dob: formatCalendarDate(slip.patient.dateOfBirth) })} />
        </Row>
        {slip.destination && <Row id="slip-lab" label={t('orders.slip.lab')}><p>{slip.destination}</p></Row>}
        <Row id="slip-tests" label={t('orders.slip.tests')}>
          <Items>{slip.orders.map((o, i) => <Item key={o.id} n={i + 1} name={o.name} detail={t(`orders.kind.${o.kind}`)}>{o.note && <p className="mt-[1pt]">{o.note}</p>}</Item>)}</Items>
        </Row>
        <Row id="slip-bring" label={t('paper.row.bring')}><p className="font-bold">{t('orders.slip.notice')}</p></Row>
      </Rows>
      {slip.doctorName && <Signature name={slip.doctorName} lines={[ordered]} />}
    </PaperPrintPage>
  );
}
