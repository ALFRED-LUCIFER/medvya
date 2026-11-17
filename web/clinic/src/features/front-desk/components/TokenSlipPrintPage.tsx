import { Link } from 'react-router';
import { Printer, Wifi } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, PageContainer, PageHeader, Detail, DetailList } from '@medvya/ui';
import { qrCells } from '../../prescription/domain/authorisation';
import { slipAheadWords, type TokenSlip } from '../domain/board';

type Props = { slip: TokenSlip | null; loading: boolean; problem: 'void' | 'forbidden' | 'failed' | null; onPrint: () => void; boardPath: string };

/**
 * The waiting-room token slip (MDV-P2-046) in the print family layout (MDV-P1-111, docs/02-design/print-family/index.html): the clinic's logo,
 * name, address and phone over a black rule; the token at the slip's full width; doctor, room, vitals room, queue and time as hanging labels;
 * a tear-off stub with the location's guest Wi-Fi (a join code phones read, the network and the password). Nothing about the patient (no name,
 * MRN, phone or reason), because the paper is handed over and left on chairs. Black only: thermal printers print one colour.
 * It prints on an 80 mm receipt page (founder decision, 15 Nov 2026), so a thermal receipt printer gives the patient a small physical token.
 * shortcut: browser print on the printer the desk chooses; a direct thermal printer integration waits for a clinic that asks for one.
 */
// Only while this page is mounted; every other print keeps A4. The page is longer than the slip, so it is always one page: 110 mm without the
// Wi-Fi stub, 160 mm with it. White, so a thermal printer does not shade the page tint; no page padding, so the slip uses the paper's width.
const receiptPage = (height: number) =>
  `@page { size: 80mm ${height}mm; margin: 0; } @media print { html, body, main { background: white !important; padding: 0 !important; } }`;

const label = 'text-[6.5pt] font-bold uppercase leading-tight tracking-[0.08em] rtl:text-[7.5pt] rtl:normal-case rtl:tracking-normal text-inherit';

export default function TokenSlipPrintPage(p: Props) {
  const i18n = useI18n(), { t, formatDate, formatTime } = i18n;
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
  const contact = [slip.address, slip.phone].filter(Boolean);
  return (
    <PageContainer width="form">
      <style>{receiptPage(slip.wifi ? 160 : 110)}</style>
      <div className="flex flex-wrap gap-3 print:hidden">
        <Button onClick={p.onPrint}><Printer aria-hidden className="size-4" />{t('tokenSlip.print')}</Button>
        {back}
      </div>
      <PageHeader title={t('tokenSlip.title')} className="print:hidden" />
      <article aria-labelledby="token-slip-token"
        className="w-[80mm] bg-white px-[4mm] pb-[6mm] pt-[5mm] text-black tabular-nums shadow-md print:shadow-none">
        <div className="flex items-center gap-[2.5mm]">
          {slip.logo && <img src={slip.logo} alt={t('tokenSlip.logo', { clinic: slip.clinicName })} className="size-[9mm] flex-none object-contain grayscale contrast-150" />}
          <div>
            <p className="text-[11pt] font-extrabold leading-[1.15]">{slip.clinicName}</p>
            {contact.length > 0 && (
              <p className="mt-[0.5mm] text-[7pt] leading-[1.3]">
                {slip.address}{slip.address && slip.phone && ' · '}{slip.phone && <bdi dir="ltr">{slip.phone}</bdi>}
              </p>
            )}
          </div>
        </div>
        <div className="mb-[3.5mm] mt-[3mm] border-t-[1.4pt] border-black" />
        {/* Print sizes are the receipt's own (mm/pt); text-inherit keeps the labels black like the rest of the slip. */}
        <DetailList layout="inline" className="grid-cols-[15mm_1fr] items-baseline gap-x-[3mm] gap-y-[2mm]">
          <Detail label={t('tokenSlip.token')} labelClassName={label} valueProps={{ id: 'token-slip-token' }} valueClassName="text-[46pt] font-extrabold leading-[0.95] tracking-[-0.02em]">
            <bdi dir="ltr">{slip.token}</bdi>
          </Detail>
          <Detail label={t('tokenSlip.doctor')} labelClassName={label} valueClassName="text-[9.5pt] font-semibold leading-[1.3]">{slip.doctorName}</Detail>
          {slip.room && <Detail label={t('tokenSlip.room')} labelClassName={label} valueClassName="text-[12pt] font-extrabold">{slip.room}</Detail>}
          {slip.vitalsRoom && <Detail label={t('tokenSlip.vitals')} labelClassName={label} valueClassName="text-[12pt] font-extrabold">{slip.vitalsRoom}</Detail>}
          {slip.ahead !== null && <Detail label={t('tokenSlip.queue')} labelClassName={label} valueClassName="text-[12pt] font-extrabold">{slipAheadWords(i18n, slip.ahead)}</Detail>}
          <Detail label={t('tokenSlip.issued')} labelClassName={label} valueClassName="text-[9.5pt] font-semibold leading-[1.3]">
            {formatDate(issued, slip.timeZone)}{' · '}<bdi dir="ltr">{formatTime(issued, slip.timeZone)}</bdi>
          </Detail>
        </DetailList>
        <p className="mt-[3.5mm] text-[8.5pt] font-semibold leading-[1.35]">{t('tokenSlip.notice')}</p>
        {slip.wifi && <WifiStub wifi={slip.wifi} />}
      </article>
    </PageContainer>
  );
}

/** Below the dashed tear line: scan the code to join, or type the password. */
function WifiStub({ wifi }: { wifi: NonNullable<TokenSlip['wifi']> }) {
  const { t } = useI18n();
  const size = wifi.qr.length;
  return (
    <section aria-labelledby="token-slip-wifi">
      <div className="relative -mx-[4mm] mb-[3.5mm] mt-[4mm] border-t-[1pt] border-dashed border-black">
        <h2 id="token-slip-wifi" className="absolute start-[4mm] top-0 -translate-y-1/2 bg-white px-[1mm] text-[6.5pt] font-bold leading-none">
          {t(wifi.daily ? 'tokenSlip.wifi.title' : 'tokenSlip.wifi.titleGuest')}
        </h2>
      </div>
      <div className="grid grid-cols-[24mm_1fr] items-start gap-x-[3.5mm]">
        <svg role="img" aria-label={t('tokenSlip.wifi.code')} viewBox={`0 0 ${size} ${size}`} className="block w-[24mm]" shapeRendering="crispEdges">
          <rect width={size} height={size} className="fill-white" />
          <g className="fill-black">{qrCells(wifi.qr).map(([x, y]) => <rect key={`${x}-${y}`} x={x} y={y} width={1} height={1} />)}</g>
        </svg>
        <div>
          <p className="mb-[1.5mm] flex items-center gap-[1.5mm] text-[9.5pt] font-extrabold"><Wifi aria-hidden className="size-[4mm]" strokeWidth={2.4} />{t('tokenSlip.wifi.scan')}</p>
          <DetailList className="gap-[1.2mm] md:grid-cols-1">
            <Detail label={t('tokenSlip.wifi.network')} labelClassName={label} valueProps={{ dir: 'ltr' }} valueClassName="break-all text-[10pt] font-extrabold rtl:text-end">{wifi.network}</Detail>
            {wifi.password && <Detail label={t('tokenSlip.wifi.password')} labelClassName={label} valueProps={{ dir: 'ltr' }} valueClassName="break-all font-mono text-[10pt] font-extrabold rtl:text-end">{wifi.password}</Detail>}
          </DetailList>
          {wifi.daily && <p className="mt-[1.5mm] text-[7pt] font-semibold">{t('tokenSlip.wifi.today')}</p>}
        </div>
      </div>
    </section>
  );
}
