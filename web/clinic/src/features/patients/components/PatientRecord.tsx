import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { ArrowLeft, Eye, TriangleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, cn } from '@medvya/ui';
import { addressLines, type PatientDetails, type RevealedIdentifiers } from '../domain/directory';
import { genderIdentities, regionKey } from '../../registration/domain/registration';
import PatientsMessage from './PatientsMessage';

type Props = {
  patient: PatientDetails | null;
  state: 'loading' | 'ready' | 'invalid' | 'forbidden' | 'notFound' | 'error';
  onRetry: () => void;
  backPath: string;
  /** Full numbers, after the audited reveal; null = masked to the last four. */
  revealed: RevealedIdentifiers | null;
  /** Only Register / book patients at Full may reveal (the server checks the same). */
  canReveal: boolean;
  revealing: boolean;
  revealFailed: boolean;
  onReveal: () => void;
};

// Keeps Latin IDs, phones and dates intact (order and line) inside Arabic text.
const Ltr = ({ children }: { children: ReactNode }) => <bdi dir="ltr">{children}</bdi>;

/** An opened record for the front desk: registration data only, never clinical data. Opening it was audited by the server. */
export default function PatientRecord(props: Props) {
  const { patient, state, onRetry, backPath } = props;
  const { t, formatDate, locale, country } = useI18n();
  const date = (iso: string) => formatDate(new Date(`${iso.slice(0, 10)}T00:00:00`));
  const back = (
    <Link to={backPath} className={cn(buttonVariants({ variant: 'ghost' }), 'self-start px-2')}>
      <ArrowLeft className="rtl:rotate-180" aria-hidden />{t('patients.record.back')}
    </Link>
  );
  // One live region that stays mounted; only its text changes, so screen readers announce every change.
  const status = state === 'loading' ? t('patients.record.loading') : state === 'forbidden' ? t('err.403')
    : state === 'notFound' ? t('patients.record.notFound.title') : state === 'error' ? t('patients.error.title')
    : props.revealing ? t('patients.loading') : props.revealed ? t('patients.record.revealed') : '';
  const live = <p role="status" className="sr-only">{status}</p>;

  if (!patient)
    return (
      <div className="flex max-w-[880px] flex-col gap-4">
        {back}
        {live}
        {state === 'loading' ? <PatientsMessage title={t('patients.record.loading')} />
          : state === 'forbidden' ? <PatientsMessage title={t('err.403')} body={t('patients.forbidden.body')} />
          : state === 'notFound' ? <PatientsMessage title={t('patients.record.notFound.title')} body={t('patients.record.notFound.body')} />
          : <PatientsMessage title={t('patients.error.title')} body={t('patients.error.body')} action={{ label: t('patients.retry'), onClick: onRetry }} />}
      </div>
    );

  const p = patient;
  const none = <span className="text-muted-foreground">{t('patients.record.none')}</span>;
  const dob = p.dateOfBirthEstimated ? t('patients.record.dobEstimated', { date: date(p.dateOfBirth) }) : date(p.dateOfBirth);
  // Codes stored at registration are shown in the UI language; older free-text values are shown as entered.
  const gender = genderIdentities.find(g => g === p.genderIdentity);
  const named = (type: 'language' | 'region', code: string) => new Intl.DisplayNames([locale], { type }).of(code) ?? code;
  const region = p.address?.region ? regionKey(country, p.address.region) : null;
  const address = p.address && region ? { ...p.address, region: t(region) } : p.address;
  const masked = (last: string) => t('patients.masked', { last });
  const full = (scheme: string) => props.revealed?.identifiers.find(i => i.scheme === scheme)?.value;
  const hasNumbers = p.identifiers.length > 0 || !!p.payer?.memberNumberLastFour;
  return (
    <div className="flex max-w-[880px] flex-col gap-4">
      {back}
      {live}
      <header className="flex flex-col gap-1 rounded-xl border bg-card p-4">
        <h1 className="text-2xl font-extrabold">
          <bdi>{p.fullName}</bdi>
          {p.arabicName && <span lang="ar" dir="rtl" className="ms-3 font-bold text-muted-foreground">{p.arabicName}</span>}
        </h1>
        <p className="text-sm text-muted-foreground">
          {t('patients.record.summary', { mrn: p.mrn, age: p.ageYears, sex: t(`patients.sex.${p.sexAtBirth}`) })}
        </p>
        {p.status === 'Incomplete' && (
          <p className="mt-1 inline-flex items-center gap-1.5 self-start rounded-sm border px-2 py-1 text-sm font-bold text-warning-700">
            <TriangleAlert className="size-4" aria-hidden />{t('patients.status.Incomplete')}
          </p>
        )}
        <p className="text-xs text-muted-foreground">{t('patients.record.noClinical')}</p>
      </header>

      <Section title={t('patients.record.identity')}>
        <Field label={t('patients.record.dob')}>{dob}</Field>
        <Field label={t('patients.record.sex')}>{t(`patients.sex.${p.sexAtBirth}`)}</Field>
        <Field label={t('patients.record.gender')}>{gender ? t(`registration.gender.${gender}`) : p.genderIdentity ?? none}</Field>
        <Field label={t('patients.record.language')}>{p.preferredLanguage ? named('language', p.preferredLanguage) : none}</Field>
        <Field label={t('patients.record.nationality')}>{p.nationality ? named('region', p.nationality) : none}</Field>
        {/* A calendar date in the facility's country time zone, from the server; never re-derived from a UTC instant. */}
        <Field label={t('patients.record.registered')}>{date(p.registeredOn)}</Field>
      </Section>

      <Section title={t('patients.record.identifiers')}>
        {p.identifiers.length === 0 ? <Field label={t('patients.record.identifiers')}>{none}</Field>
          : p.identifiers.map(i => (
            <Field key={i.scheme} label={t(`patients.scheme.${i.scheme}`)}>
              <Ltr>{full(i.scheme) ?? masked(i.lastFour)}</Ltr>
              {i.expiresOn && <span className="ms-1 text-muted-foreground">{t('patients.record.expires', { date: date(i.expiresOn) })}</span>}
            </Field>
          ))}
      </Section>

      <Section title={t('patients.record.contact')}>
        <Field label={t('patients.record.mobile')}><Ltr>{p.mobile}</Ltr></Field>
        <Field label={t('patients.record.alternate')}>{p.alternatePhone ? <Ltr>{p.alternatePhone}</Ltr> : none}</Field>
        <Field label={t('patients.record.email')}>{p.email ? <Ltr>{p.email}</Ltr> : none}</Field>
        <Field label={t('patients.record.address')}>{address ? <bdi>{addressLines(address).join(', ')}</bdi> : none}</Field>
      </Section>

      <Section title={t('patients.record.payer')}>
        {p.payer ? <>
          <Field label={t('patients.record.payerKind')}>{t(`patients.payer.${p.payer.kind}`)}</Field>
          {p.payer.payerName && <Field label={t('patients.record.payerName')}><bdi>{p.payer.payerName}</bdi></Field>}
          {p.payer.plan && <Field label={t('patients.record.plan')}><bdi>{p.payer.plan}</bdi></Field>}
          {p.payer.memberNumberLastFour && (
            <Field label={t('patients.record.memberNumber')}><Ltr>{props.revealed?.memberNumber ?? masked(p.payer.memberNumberLastFour)}</Ltr></Field>
          )}
          {p.payer.validTo && <Field label={t('patients.record.validTo')}>{date(p.payer.validTo)}</Field>}
        </> : <Field label={t('patients.record.payerKind')}>{t('patients.record.payerMissing')}</Field>}
      </Section>

      {props.canReveal && hasNumbers && !props.revealed && (
        <div className="flex flex-col items-start gap-1">
          <Button variant="outline" loading={props.revealing} onClick={props.onReveal}><Eye aria-hidden />{t('patients.record.reveal')}</Button>
          <p className="text-xs text-muted-foreground">{t('patients.record.revealNote')}</p>
          {props.revealFailed && <p className="text-sm font-semibold text-danger-700">{t('patients.record.revealFailed')}</p>}
        </div>
      )}
    </div>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="rounded-xl border bg-card p-4">
      <h2 className="mb-3 text-base font-extrabold">{title}</h2>
      <dl className="grid gap-x-6 gap-y-3 sm:grid-cols-2">{children}</dl>
    </section>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex min-w-0 flex-col">
      <dt className="text-xs font-bold text-muted-foreground">{label}</dt>
      <dd className="break-words">{children}</dd>
    </div>
  );
}
