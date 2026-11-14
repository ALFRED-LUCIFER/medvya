import { Link } from 'react-router';
import { Printer } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, PageContainer, PageHeader } from '@medvya/ui';
import type { TokenSlip } from '../domain/board';

type Props = { slip: TokenSlip | null; loading: boolean; problem: 'void' | 'forbidden' | 'failed' | null; onPrint: () => void; boardPath: string };

/**
 * The waiting-room token slip (MDV-P2-046), in the house print style: clinic, token, doctor, date and time, and nothing about the
 * patient (no name, MRN, phone or reason), because the paper is handed over and left on chairs. The page prints; the shell hides itself.
 * shortcut: browser print on any printer; a thermal printer integration waits for a clinic that asks for one.
 */
export default function TokenSlipPrintPage(p: Props) {
  const { t, formatDate, formatTime } = useI18n();
  const back = <Link to={p.boardPath} className={buttonVariants({ variant: 'outline' })}>{t('tokenSlip.back')}</Link>;
  if (p.loading) return <PageContainer width="form"><p role="status">{t('tokenSlip.loading')}</p></PageContainer>;
  if (p.problem || !p.slip)
    return (
      <PageContainer width="form">
        <PageHeader title={t('tokenSlip.title')} />
        <p role="alert" className="font-bold">{t(`tokenSlip.problem.${p.problem ?? 'failed'}`)}</p>
        <div>{back}</div>
      </PageContainer>
    );
  const { slip } = p;
  const issued = new Date(slip.issuedAt);
  return (
    <PageContainer width="form">
      <div className="flex flex-wrap gap-3 print:hidden">
        <Button onClick={p.onPrint}><Printer aria-hidden className="size-4" />{t('tokenSlip.print')}</Button>
        {back}
      </div>
      <article aria-labelledby="token-slip-token" className="flex max-w-panel flex-col gap-3 rounded-xl border bg-white p-6 text-black print:rounded-none print:border-0 print:p-0">
        <PageHeader title={t('tokenSlip.title')} description={<span className="font-bold text-black">{slip.clinicName}</span>} />
        <p id="token-slip-token" className="text-display">
          <span className="sr-only">{t('tokenSlip.token')} </span><bdi dir="ltr" className="font-mono">{slip.token}</bdi>
        </p>
        <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1">
          <dt className="font-bold">{t('tokenSlip.doctor')}</dt><dd>{slip.doctorName}</dd>
          <dt className="font-bold">{t('tokenSlip.issued')}</dt>
          <dd className="tabular-nums">{`${formatDate(issued, slip.timeZone)} ${formatTime(issued, slip.timeZone)}`}</dd>
        </dl>
        <p className="border-t border-black pt-2 text-caption">{t('tokenSlip.notice')}</p>
      </article>
    </PageContainer>
  );
}
