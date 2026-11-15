import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { FileDown, Printer, ShieldCheck } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Field, PageHeader, PageMessage, SegmentedControl, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { secondLanguages, type PaperSize, type RxLayout, type RxPrint, type RxPrintLine, type SecondLanguage } from '../domain/authorisation';
import PaperPrintPage, { Allergy, Ident, Item, Items, Masthead, Meta, Mono, PaperFooter, Row, Rows, Signature, Warning } from './PaperPrintPage';
import { directionOf } from '../domain/prescription';

type Props = {
  print: RxPrint | null; loading: boolean; notAuthorised: boolean; cancelled: boolean; failed: boolean;
  second: SecondLanguage; onSecond: (l: SecondLanguage) => void; paper: PaperSize; onPaper: (s: PaperSize) => void;
  onPrint: () => void; onPdf: (paper: 'A4' | 'A5') => void; downloading: 'A4' | 'A5' | null; pdfProblem: 'unsupported' | 'failed' | null;
  layout: RxLayout | null; approving: boolean; layoutFailed: boolean; onApprove: () => void;
  consultPath: string;
  /** The share panel (MDV-P2-020), under the paper once it prints. */
  share?: ReactNode;
};

/**
 * E-10 printed prescription (MDV-P2-017) in the print family layout (MDV-P1-111): masthead, labelled rows, numbered medicines, signature box
 * and verify footer, as headings, lists and text a screen reader reads in order (reflows at 200 % zoom). Each instruction is in English plus
 * the chosen second language (Arabic right to left). Print uses the browser on the chosen paper; the PDF is the server's stored English copy. The doctor approves the layout as their own once (exit gate 4).
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
  const medicines = v.lines.filter(l => !l.discontinue), stops = v.lines.filter(l => l.discontinue);
  const toolbar = <>
    <div className="flex flex-wrap items-end gap-3">
      <Select value={p.second} onValueChange={l => p.onSecond(l as SecondLanguage)}>
        <Field id="rx-second" label={t('rxPrint.second')} className="min-w-56">
          <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
        </Field>
        <SelectContent>{secondLanguages.map(l => <SelectItem key={l} value={l}>{l === 'none' ? t('rxPrint.secondNone') : t(`rx.lang.${l}`)}</SelectItem>)}</SelectContent>
      </Select>
      <SegmentedControl label={t('rxPrint.paper')} value={p.paper} onValueChange={p.onPaper} options={papers.map(s => ({ value: s, label: s }))} />
      <Button onClick={p.onPrint}><Printer aria-hidden className="size-4" />{t('rxPrint.print')}</Button>
      {papers.map(paper => (
        <Button key={paper} variant="outline" loading={p.downloading === paper} disabled={p.downloading !== null} onClick={() => p.onPdf(paper)}>
          <FileDown aria-hidden className="size-4" />{t(paper === 'A4' ? 'rxPrint.pdfA4' : 'rxPrint.pdfA5')}
        </Button>
      ))}
      <Back {...p} />
    </div>
    {p.second !== 'none' && <p className="text-sm">{t('rxPrint.pdfEnglish', { language: t(`rx.lang.${p.second}`) })}</p>}
    {p.pdfProblem && <p role="alert" className="font-bold text-danger-700">{t(p.pdfProblem === 'unsupported' ? 'rxPrint.pdfUnsupported' : 'rxPrint.pdfFailed')}</p>}
  </>;
  const after = <>
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
    {p.share && <div className="print:hidden">{p.share}</div>}
  </>;
  return (
    <PaperPrintPage size={p.paper} label={v.clinic} toolbar={toolbar} after={after}>
      {/* shortcut: the print data has no clinic logo yet (the server PDF prints it); add it to RxPrint if the browser print must carry it. */}
      <Masthead clinic={v.clinic} address={[v.clinicAddress, v.clinicPhone, v.clinicRegistration && t('rxPrint.registration', { number: ltr(v.clinicRegistration) })].filter(Boolean).join(sep) || null} doctor={{ name: v.doctor, lines: [v.qualifications, t('paper.regAuthority', { number: ltr(v.licenceNumber), authority: v.licenceAuthority })] }} />
      <Meta title={t('rxPrint.title')}><Mono>{v.authorisation.number}</Mono>{sep}{t('paper.rxMeta', { version: v.authorisation.versionNo, when, zone: v.timeZone })}</Meta>
      <Rows>
        <Row id="rx-patient" label={t('paper.row.patient')}>
          <Ident name={v.patient.fullName} sub={t('paper.ident', { sex: t(`rxPrint.sex.${v.patient.sexAtBirth}`), age: v.patientAge, mrn: ltr(v.patient.mrn), dob: formatCalendarDate(v.patient.dateOfBirth) })} />
        </Row>
        <Row id="rx-allergy" label={t('paper.row.allergy')}>
          {v.allergies === 'Listed' ? <Allergy>{v.allergySubstances.join(', ')}</Allergy>
            : <p className="font-bold">{t(v.allergies === 'NoKnownAllergies' ? 'rxPrint.allergiesNone' : 'rxPrint.allergiesNotRecorded')}</p>}
        </Row>
        {v.diagnoses.length > 0 && (
          <Row id="rx-diagnosis" label={t('paper.row.diagnosis')}>
            <p>{v.diagnoses.map(d => (d.provisional ? t('rxPrint.diagnosisProvisional', { name: d.description }) : d.description)).join(sep)}</p>
          </Row>
        )}
        {medicines.length > 0 && (
          <Row id="rx-print-medicines" label={t('rxPrint.medicines')}>
            <Items>{medicines.map((l, i) => <Medicine key={i} n={i + 1} line={l} second={p.second} />)}</Items>
          </Row>
        )}
        {stops.length > 0 && (
          <Row id="rx-stop" label={t('paper.row.stop')}>
            {stops.map(l => (
              <p key={l.medicine}>
                <span className="font-extrabold"><span className="uppercase">{l.medicine}</span>{l.strength && ` ${l.strength}`}</span>
                {l.reason && <span className="text-ink-700">{sep}{t('rxPrint.reason', { reason: l.reason })}</span>}
              </p>
            ))}
          </Row>
        )}
        {v.advice?.text && (
          <Row id="rx-print-advice" label={t('paper.row.advice')}>
            <p lang="en" dir="ltr" className="whitespace-pre-line rtl:text-right">{v.advice.text}</p>
            {!v.advice.inPatientLanguage && <p className="text-[7.6pt] text-ink-600">{t('rxPrint.adviceInEnglish')}</p>}
          </Row>
        )}
        {v.followUpOn && <Row id="rx-follow-up" label={t('paper.row.followUp')}><p className="font-bold">{t('paper.followUpOn', { date: formatCalendarDate(v.followUpOn) })}</p></Row>}
      </Rows>
      <Signature name={v.doctor} lines={[t('paper.reg', { number: ltr(v.licenceNumber) }), t('paper.authorised', { when })]} />
      <PaperFooter qr={v.qr} qrLabel={t('rxPrint.qr')} title={t('paper.checkRx')} end={<Mono>{v.authorisation.number}</Mono>}>
        <p>{t('paper.scanOrOpen')} <Mono wrap>{v.verifyUrl}</Mono></p>
      </PaperFooter>
    </PaperPrintPage>
  );
}

const papers = ['A4', 'A5'] as const;
const sep = ' · ';

function Back({ consultPath }: { consultPath: string }) {
  const { t } = useI18n();
  return <Link to={consultPath} className={buttonVariants({ variant: 'outline', className: 'self-start' })}>{t('rxPrint.back')}</Link>;
}

/** Generic name first in capitals (the composer prescribes by generic), strength and form after it, the English instruction, the second language's
 * under it, then the class's notes, each in its box. */
function Medicine({ n, line, second }: { n: number; line: RxPrintLine; second: SecondLanguage }) {
  const { t } = useI18n();
  return (
    <Item n={n} caps name={line.medicine} detail={[line.strength, line.form].filter(Boolean).join(' ')}>
      <p lang="en" dir="ltr" className="mt-[1pt] rtl:text-right">{line.sig.en}</p>
      {second !== 'none' && line.sig[second] && <p lang={second} dir={directionOf(second)}>{line.sig[second]}</p>}
      {line.reason && <p className="text-ink-700">{t('rxPrint.reason', { reason: line.reason })}</p>}
      {line.printRequirements.map(r => <Warning key={r}>{t(`rxPrint.requirement.${r}`)}</Warning>)}
    </Item>
  );
}
