import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { ArrowLeft, TriangleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { buttonVariants, cn } from '@medvya/ui';
import { addressLines, type PatientDetails } from '../domain/directory';
import PatientsMessage from './PatientsMessage';

type Props = {
  patient: PatientDetails | null;
  state: 'loading' | 'ready' | 'invalid' | 'forbidden' | 'notFound' | 'error';
  onRetry: () => void;
  backPath: string;
};

// Keeps Latin IDs, phones and dates intact (order and line) inside Arabic text.
const Ltr = ({ children }: { children: ReactNode }) => <bdi dir="ltr">{children}</bdi>;

/** An opened record for the front desk: registration data only, never clinical data. Opening it was audited by the server. */
export default function PatientRecord({ patient, state, onRetry, backPath }: Props) {
  const { t, formatDate } = useI18n();
  const date = (iso: string) => formatDate(new Date(`${iso.slice(0, 10)}T00:00:00`));
  const back = (
    <Link to={backPath} className={cn(buttonVariants({ variant: 'ghost' }), 'self-start px-2')}>
      <ArrowLeft className="rtl:rotate-180" aria-hidden />{t('patients.record.back')}
    </Link>
  );

  if (!patient)
    return (
      <div className="flex max-w-[880px] flex-col gap-4">
        {back}
        {state === 'loading' ? <PatientsMessage busy title={t('patients.record.loading')} />
          : state === 'forbidden' ? <PatientsMessage title={t('err.403')} body={t('patients.forbidden.body')} />
          : state === 'notFound' ? <PatientsMessage title={t('patients.record.notFound.title')} body={t('patients.record.notFound.body')} />
          : <PatientsMessage alert title={t('patients.error.title')} body={t('patients.error.body')} action={{ label: t('patients.retry'), onClick: onRetry }} />}
      </div>
    );

  const p = patient;
  const none = <span className="text-muted-foreground">{t('patients.record.none')}</span>;
  const dob = p.dateOfBirthEstimated ? t('patients.record.dobEstimated', { date: date(p.dateOfBirth) }) : date(p.dateOfBirth);
  return (
    <div className="flex max-w-[880px] flex-col gap-4">
      {back}
      <header className="flex flex-col gap-1 rounded-xl border bg-card p-4">
        <h1 className="text-2xl font-extrabold">
          <bdi>{p.fullName}</bdi>
          {p.arabicName && <span lang="ar" dir="rtl" className="ms-3 font-bold text-muted-foreground">{p.arabicName}</span>}
        </h1>
        <p className="text-sm text-muted-foreground">
          {t('patients.record.summary', { mrn: p.mrn, age: p.ageYears, sex: t(`patients.sex.${p.sexAtBirth}`) })}
        </p>
        {p.status === 'Incomplete' && (
          <p className="mt-1 inline-flex items-center gap-1.5 self-start rounded-sm border border-line-strong px-2 py-1 text-sm font-bold text-warning-700">
            <TriangleAlert className="size-4" aria-hidden />{t('patients.status.Incomplete')}
          </p>
        )}
        <p className="text-xs text-muted-foreground">{t('patients.record.noClinical')}</p>
      </header>

      <Section title={t('patients.record.identity')}>
        <Field label={t('patients.record.dob')}>{dob}</Field>
        <Field label={t('patients.record.sex')}>{t(`patients.sex.${p.sexAtBirth}`)}</Field>
        <Field label={t('patients.record.gender')}>{p.genderIdentity ?? none}</Field>
        <Field label={t('patients.record.language')}>{p.preferredLanguage ?? none}</Field>
        <Field label={t('patients.record.nationality')}>{p.nationality ?? none}</Field>
        <Field label={t('patients.record.registered')}>{date(p.registeredAt)}</Field>
      </Section>

      <Section title={t('patients.record.identifiers')}>
        {p.identifiers.length === 0 ? <Field label={t('patients.record.identifiers')}>{none}</Field>
          : p.identifiers.map(i => (
            <Field key={i.scheme} label={t(`patients.scheme.${i.scheme}`)}>
              <Ltr>{i.value}</Ltr>{i.expiresOn && <span className="ms-1 text-muted-foreground">{t('patients.record.expires', { date: date(i.expiresOn) })}</span>}
            </Field>
          ))}
      </Section>

      <Section title={t('patients.record.contact')}>
        <Field label={t('patients.record.mobile')}><Ltr>{p.mobile}</Ltr></Field>
        <Field label={t('patients.record.alternate')}>{p.alternatePhone ? <Ltr>{p.alternatePhone}</Ltr> : none}</Field>
        <Field label={t('patients.record.email')}>{p.email ? <Ltr>{p.email}</Ltr> : none}</Field>
        <Field label={t('patients.record.address')}>{p.address ? <bdi>{addressLines(p.address).join(', ')}</bdi> : none}</Field>
      </Section>

      <Section title={t('patients.record.payer')}>
        {p.payer ? <>
          <Field label={t('patients.record.payerKind')}>{t(`patients.payer.${p.payer.kind}`)}</Field>
          {p.payer.payerName && <Field label={t('patients.record.payerName')}><bdi>{p.payer.payerName}</bdi></Field>}
          {p.payer.plan && <Field label={t('patients.record.plan')}><bdi>{p.payer.plan}</bdi></Field>}
          {p.payer.memberNumber && <Field label={t('patients.record.memberNumber')}><Ltr>{p.payer.memberNumber}</Ltr></Field>}
          {p.payer.validTo && <Field label={t('patients.record.validTo')}>{date(p.payer.validTo)}</Field>}
        </> : <Field label={t('patients.record.payerKind')}>{t('patients.record.payerMissing')}</Field>}
      </Section>
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
