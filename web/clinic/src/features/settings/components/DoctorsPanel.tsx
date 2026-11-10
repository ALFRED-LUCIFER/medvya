import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { BadgeCheck, CalendarX, Info } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Input } from '@medvya/ui';
import type { Authority, Doctor, DoctorForm, DoctorStep, FieldProblem, LicenceForm } from '../domain/doctors';
import type { Location } from '../domain/settings';
import DoctorWizard, { LicenceFields } from './DoctorWizard';
import { describedBy, fieldError } from './fieldStyles';
import { Field } from './Field';
import { Section } from './Section';

type Wizard = {
  adding: boolean; step: DoctorStep; stepIndex: number; lastStep: boolean; form: UseFormReturn<DoctorForm>; fieldErrors: Record<string, FieldProblem | undefined>;
  submitting: boolean; submitFailed: boolean; emailFailed: boolean; signature: File | null; signatureProblem: 'type' | 'size' | null;
  onPickSignature: (file: File | undefined) => void; onBack: () => void; onNext: () => void; onClose: () => void; onSubmit: FormEventHandler<HTMLFormElement>;
};

type Props = Wizard & {
  loading: boolean;
  forbidden: boolean;
  loadFailed: boolean;
  doctors: Doctor[];
  authorities: Authority[];
  locations: Location[];
  currency: string;
  onRetry: () => void;
  added: { name: string; signatureFailed: boolean } | null;
  onDismissAdded: () => void;
  onOpen: () => void;
  correcting: Doctor | null;
  licenceForm: UseFormReturn<LicenceForm>;
  licenceErrors: Record<string, FieldProblem | undefined>;
  onCorrect: (d: Doctor | null) => void;
  onSubmitLicence: FormEventHandler<HTMLFormElement>;
  correctingBusy: boolean;
  correctFailed: boolean;
  rowProblem: { id: string; kind: 'type' | 'size' | 'failed' } | null;
  busyId: string | null;
  onSignature: (id: string, file: File | undefined) => void;
  onRemoveSignature: (id: string) => void;
};

/** Who attested the licence and when, or that it has expired: words and an icon, never colour alone. Medvya does not check licences (ADR-016). */
function LicenceBadge({ doctor: d }: { doctor: Doctor }) {
  const { t } = useI18n();
  const Icon = d.expired ? CalendarX : BadgeCheck;
  const date = d.attestedAt.slice(0, 10);
  const label = d.expired ? t('doctors.licence.expired') : d.attestedByName ? t('doctors.licence.attestedBy', { name: d.attestedByName, date }) : t('doctors.licence.attestedByOwner', { date });
  return <span className="inline-flex items-center gap-1.5 font-bold"><Icon className="size-4 shrink-0" aria-hidden />{label}</span>;
}

/** Doctors (F-09, MDV-P1-007): the doctors of the practice with their licence state, and the Add doctor wizard. */
export default function DoctorsPanel(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.loadFailed) {
    return (
      <div className="flex flex-col items-start gap-3">
        <p role="alert" className={fieldError}>{t(p.forbidden ? 'settings.forbidden' : 'settings.loadFailed')}</p>
        {!p.forbidden && <Button type="button" variant="outline" className="min-h-11" onClick={p.onRetry}>{t('doctors.retry')}</Button>}
      </div>
    );
  }
  return (
    <div className="flex flex-col gap-4">
      {p.added && (
        <p role="status" className="flex items-start gap-2 rounded-md border bg-muted p-3">
          <Info className="mt-0.5 size-4 shrink-0" aria-hidden />
          <span>{t('doctors.added', { name: p.added.name })}{p.added.signatureFailed ? ` ${t('doctors.added.signatureFailed')}` : ''}</span>
          <Button type="button" variant="ghost" className="ms-auto min-h-11" onClick={p.onDismissAdded}>{t('doctors.dismiss')}</Button>
        </p>
      )}
      {p.adding
        ? <DoctorWizard {...p} />
        : (
          <Section title={t('doctors.title')} intro={t('doctors.intro')}>
            <div><Button type="button" className="min-h-11" onClick={p.onOpen}>{t('doctors.add')}</Button></div>
            {p.doctors.length === 0 && <p className="text-muted-foreground">{t('doctors.empty')}</p>}
            <ul className="flex flex-col gap-3">
              {p.doctors.map(d => <DoctorRow key={d.id} doctor={d} {...p} />)}
            </ul>
          </Section>
        )}
    </div>
  );
}

function DoctorRow({ doctor: d, ...p }: Props & { doctor: Doctor }) {
  const { t } = useI18n();
  const problem = p.rowProblem?.id === d.id ? p.rowProblem.kind : null;
  const busy = p.busyId === d.id;
  const editing = p.correcting?.id === d.id;
  return (
    <li className="flex flex-col gap-3 rounded-lg border p-4">
      <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
        <h3 className="text-base font-extrabold">{d.fullName}</h3>
        <LicenceBadge doctor={d} />
        <span className="text-muted-foreground">{t(`doctors.status.${d.status}`)}</span>
      </div>
      <p className="text-sm text-muted-foreground">{[d.specialty, d.facilityName].filter(Boolean).join(' · ')}</p>
      <p className="text-sm"><span dir="ltr">{d.authority} {d.number}</span>{d.expiresOn ? ` · ${t('doctors.licence.until', { date: d.expiresOn })}` : ''}</p>
      {d.expired && <p role="note" className="text-sm font-semibold">{t('doctors.licence.expiredNote')}</p>}
      {!editing && <div><Button type="button" variant="outline" className="min-h-11" onClick={() => p.onCorrect(d)}>{t(d.expired ? 'doctors.licence.renew' : 'doctors.licence.correct')}</Button></div>}
      {editing && (
        <form noValidate onSubmit={p.onSubmitLicence} className="grid gap-4 sm:grid-cols-2" aria-label={t('doctors.licence.correct')}>
          <LicenceFields form={p.licenceForm} errors={p.licenceErrors} authorities={p.authorities} prefix={`c-${d.id}`} />
          <div className="flex flex-wrap items-center gap-3 sm:col-span-2" aria-live="polite">
            <Button type="submit" className="min-h-11" loading={p.correctingBusy}>{t('doctors.licence.resubmit')}</Button>
            <Button type="button" variant="ghost" className="min-h-11" onClick={() => p.onCorrect(null)}>{t('settings.cancel')}</Button>
            {p.correctFailed && <span role="alert" className={fieldError}>{t('settings.saveFailed')}</span>}
          </div>
        </form>
      )}
      <div className="flex flex-col gap-2">
        <p className="font-bold">{d.hasSignature ? t('doctors.signature.has') : t('doctors.signature.none')}</p>
        <p className="text-sm text-muted-foreground">{t('doctors.signature.notice')}</p>
        <div className="flex flex-wrap items-end gap-3">
          <Field id={`sig-${d.id}`} label={d.hasSignature ? t('doctors.signature.replace') : t('doctors.signature.choose')} hint={t('doctors.signature.hint')}
            error={problem ? t(`doctors.signature.error.${problem}`) : null} className="min-w-60">
            <Input id={`sig-${d.id}`} type="file" accept="image/png,image/jpeg" className="h-auto min-h-11 py-2" disabled={busy} {...describedBy(`sig-${d.id}`, !!problem, true)}
              onChange={e => { p.onSignature(d.id, e.currentTarget.files?.[0]); e.currentTarget.value = ''; }} />
          </Field>
          {d.hasSignature && <Button type="button" variant="outline" className="min-h-11" loading={busy} onClick={() => p.onRemoveSignature(d.id)}>{t('doctors.signature.remove')}</Button>}
        </div>
      </div>
    </li>
  );
}
