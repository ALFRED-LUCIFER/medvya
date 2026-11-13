import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { FileDown, Printer, ShieldCheck } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Field, PageHeader, PageMessage, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { qrCells, secondLanguages, type RxLayout, type RxPrint, type RxPrintLine, type SecondLanguage } from '../domain/authorisation';
import { directionOf } from '../domain/prescription';

type Props = {
  print: RxPrint | null; loading: boolean; notAuthorised: boolean; cancelled: boolean; failed: boolean;
  second: SecondLanguage; onSecond: (l: SecondLanguage) => void;
  onPrint: () => void; onPdf: (paper: 'A4' | 'A5') => void; downloading: 'A4' | 'A5' | null; pdfProblem: 'unsupported' | 'failed' | null;
  layout: RxLayout | null; approving: boolean; layoutFailed: boolean; onApprove: () => void;
  consultPath: string;
  /** The share panel (MDV-P2-020), under the paper once it prints. */
  share?: ReactNode;
};

/**
 * E-10 printed prescription (MDV-P2-017): the authorised prescription in black on white, as plain headings, lists and text (readable by a screen
 * reader, reflows at 200 % zoom). Each instruction is in English plus the chosen second language (Arabic right to left). Print uses the browser;
 * the PDF is the server's stored English copy. The doctor approves the layout as their own once (exit gate 4).
 */
export default function RxPrintPage(p: Props) {
  const { t, formatDate, formatTime, formatCalendarDate } = useI18n();
  if (p.loading || p.notAuthorised || p.failed || !p.print)
    return (
      <div className="flex max-w-[720px] flex-col gap-4">
        <PageHeader title={t('rxPrint.title')} />
        {p.loading ? <PageMessage kind="busy">{t('rxPrint.loading')}</PageMessage>
          : p.notAuthorised ? <PageMessage kind="error" action={<Back {...p} />}>{t(p.cancelled ? 'rxPrint.cancelled' : 'rxPrint.notAuthorised')}</PageMessage>
          : <PageMessage kind="error">{t('rxPrint.loadFailed')}</PageMessage>}
      </div>
    );
  const v = p.print;
  const at = new Date(v.authorisation.authorisedAt);
  const when = `${formatDate(at, v.timeZone)} ${formatTime(at, v.timeZone)}`;
  const size = v.qr.length;
  return (
    <div className="flex max-w-[720px] flex-col gap-4">
      <div className="flex flex-wrap items-end gap-3 print:hidden">
        <Select value={p.second} onValueChange={l => p.onSecond(l as SecondLanguage)}>
          <Field id="rx-second" label={t('rxPrint.second')} className="min-w-56">
            <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
          </Field>
          <SelectContent>{secondLanguages.map(l => <SelectItem key={l} value={l}>{l === 'none' ? t('rxPrint.secondNone') : t(`rx.lang.${l}`)}</SelectItem>)}</SelectContent>
        </Select>
        <Button onClick={p.onPrint}><Printer aria-hidden className="size-4" />{t('rxPrint.print')}</Button>
        {(['A4', 'A5'] as const).map(paper => (
          <Button key={paper} variant="outline" loading={p.downloading === paper} disabled={p.downloading !== null} onClick={() => p.onPdf(paper)}>
            <FileDown aria-hidden className="size-4" />{t(paper === 'A4' ? 'rxPrint.pdfA4' : 'rxPrint.pdfA5')}
          </Button>
        ))}
        <Back {...p} />
      </div>
      {p.second !== 'none' && <p className="text-sm print:hidden">{t('rxPrint.pdfEnglish', { language: t(`rx.lang.${p.second}`) })}</p>}
      {p.pdfProblem && <p role="alert" className="font-bold text-danger-700 print:hidden">{t(p.pdfProblem === 'unsupported' ? 'rxPrint.pdfUnsupported' : 'rxPrint.pdfFailed')}</p>}

      <article aria-label={v.clinic} className="flex flex-col gap-4 rounded-xl border bg-white p-6 text-black print:rounded-none print:border-0 print:p-0">
        {/* On paper the clinic name wraps; it is never cut. */}
        <PageHeader className="border-b-2 border-black pb-2 [&_h1]:whitespace-normal" title={v.clinic}>
          <p className="font-bold">{v.qualifications ? t('rxPrint.doctor', { name: v.doctor, qualifications: v.qualifications }) : v.doctor}</p>
          <p>{t('rxPrint.licence', { number: ltr(v.licenceNumber), authority: v.licenceAuthority })}</p>
          <p>{t('rxPrint.patient', { name: v.patient.fullName, sex: t(`rxPrint.sex.${v.patient.sexAtBirth}`), age: v.patientAge, mrn: ltr(v.patient.mrn), dob: formatCalendarDate(v.patient.dateOfBirth) })}</p>
          <p>{t('rxPrint.number', { number: ltr(v.authorisation.number), version: v.authorisation.versionNo, when, zone: v.timeZone })}</p>
        </PageHeader>
        <p className="font-bold">
          {v.allergies === 'Listed' ? t('rxPrint.allergies', { list: v.allergySubstances.join(', ') })
            : t(v.allergies === 'NoKnownAllergies' ? 'rxPrint.allergiesNone' : 'rxPrint.allergiesNotRecorded')}
        </p>
        {v.diagnoses.length > 0 && <p>{t('rxPrint.diagnosis', { list: v.diagnoses.map(d => (d.provisional ? t('rxPrint.diagnosisProvisional', { name: d.description }) : d.description)).join(', ') })}</p>}
        <section aria-labelledby="rx-print-medicines">
          <h2 id="rx-print-medicines" className="mb-1 font-extrabold">{t('rxPrint.medicines')}</h2>
          <ol className="flex list-decimal flex-col gap-2 ps-6">
            {v.lines.filter(l => !l.discontinue).map((l, i) => <Medicine key={i} line={l} second={p.second} />)}
          </ol>
          {v.lines.filter(l => l.discontinue).map(l => (
            <div key={l.medicine} className="mt-2">
              <p className="font-bold">{t('rxPrint.stop', { medicine: l.medicine })}</p>
              {l.reason && <p>{t('rxPrint.reason', { reason: l.reason })}</p>}
            </div>
          ))}
        </section>
        {v.advice?.text && (
          <section aria-labelledby="rx-print-advice">
            <h2 id="rx-print-advice" className="mb-1 font-extrabold">{t('rxPrint.advice')}</h2>
            <p lang="en" dir="ltr" className="whitespace-pre-line">{v.advice.text}</p>
            {!v.advice.inPatientLanguage && <p className="text-sm">{t('rxPrint.adviceInEnglish')}</p>}
          </section>
        )}
        {v.followUpOn && <p className="font-bold">{t('rxPrint.followUp', { date: formatCalendarDate(v.followUpOn) })}</p>}
        <footer className="flex flex-wrap items-end gap-4 border-t border-black pt-2">
          <svg role="img" aria-label={t('rxPrint.qr')} viewBox={`0 0 ${size} ${size}`} className="size-32 shrink-0" shapeRendering="crispEdges">
            <rect width={size} height={size} className="fill-white" />
            <g className="fill-black">{qrCells(v.qr).map(([x, y]) => <rect key={`${x}-${y}`} x={x} y={y} width={1} height={1} />)}</g>
          </svg>
          <div className="flex min-w-0 flex-1 flex-col gap-1 text-sm">
            <p>{t('rxPrint.signature')}</p>
            <p className="font-bold">{t('rxPrint.authorised', { name: v.doctor, when })}</p>
            <p className="break-all">{t('rxPrint.verify', { url: v.verifyUrl })}</p>
          </div>
        </footer>
      </article>

      <section aria-labelledby="rx-layout" className="flex flex-col gap-2 rounded-xl border p-4 print:hidden">
        <h2 id="rx-layout" className="font-extrabold">{t('rxPrint.layout.title')}</h2>
        {p.layout?.approvedAt
          ? <p role="status" className="flex items-center gap-2"><ShieldCheck aria-hidden className="size-4" />{t('rxPrint.layout.approved', { version: p.layout.layoutVersion, date: formatDate(new Date(p.layout.approvedAt), v.timeZone) })}</p>
          : <>
            <p className="text-sm">{t('rxPrint.layout.intro')}</p>
            <Button variant="outline" className="self-start" loading={p.approving} onClick={p.onApprove}>{t('rxPrint.layout.approve')}</Button>
          </>}
        {p.layoutFailed && <p role="alert" className="font-bold text-danger-700">{t('rxPrint.layout.failed')}</p>}
      </section>
      {p.share}
    </div>
  );
}

function Back({ consultPath }: { consultPath: string }) {
  const { t } = useI18n();
  return <Link to={consultPath} className={buttonVariants({ variant: 'outline', className: 'self-start' })}>{t('rxPrint.back')}</Link>;
}

/** Generic name first (the composer prescribes by generic), the English instruction, the second language's beside it, then the class's notes. */
function Medicine({ line, second }: { line: RxPrintLine; second: SecondLanguage }) {
  const { t } = useI18n();
  return (
    <li>
      <p className="font-bold">{[line.medicine, line.strength, line.form].filter(Boolean).join(' ')}</p>
      <p lang="en" dir="ltr">{line.sig.en}</p>
      {second !== 'none' && line.sig[second] && <p lang={second} dir={directionOf(second)}>{line.sig[second]}</p>}
      {line.printRequirements.map(r => <p key={r} className="text-sm">{t(`rxPrint.requirement.${r}`)}</p>)}
      {line.reason && <p className="text-sm">{t('rxPrint.reason', { reason: line.reason })}</p>}
    </li>
  );
}
