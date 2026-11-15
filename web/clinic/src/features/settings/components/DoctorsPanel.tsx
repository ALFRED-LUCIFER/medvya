import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { BadgeCheck, CalendarX, Info } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Field, Input, Tag } from '@medvya/ui';
import type { Authority, Doctor, DoctorForm, DoctorStep, FieldProblem, LicenceForm } from '../domain/doctors';
import type { Location } from '../domain/settings';
import { licenceFields } from '../domain/doctors';
import DoctorWizard, { LicenceFields, StepErrorSummary } from './DoctorWizard';
import { fieldError, fieldGrid } from './fieldStyles';
import { Section } from './Section';
import SignatureImage from './SignatureImage';

type Wizard = {
  adding: boolean; step: DoctorStep; stepIndex: number; lastStep: boolean; form: UseFormReturn<DoctorForm>; fieldErrors: Record<string, FieldProblem | undefined>; errorFocusKey: number;
  submitting: boolean; submitFailed: boolean; emailFailed: boolean; planFull: string | null; canContinue: boolean;
  onBack: () => void; onNext: () => void; onClose: () => void; onSubmit: FormEventHandler<HTMLFormElement>;
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
  added: { name: string } | null;
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
  roomBusyId: string | null;
  roomSavedId: string | null;
  roomFailedId: string | null;
  onRoom: (id: string, room: string) => void;
};

/** Who attested the licence and when, or that it has expired: words and an icon, never colour alone. Medvya does not check licences (ADR-016). */
function LicenceBadge({ doctor: d }: { doctor: Doctor }) {
  const { t, formatDate } = useI18n();
  const Icon = d.expired ? CalendarX : BadgeCheck;
  const date = formatDate(new Date(d.attestedAt));
  const label = d.expired ? t('doctors.licence.expired') : d.attestedByName ? t('doctors.licence.attestedBy', { name: d.attestedByName, date }) : t('doctors.licence.attestedByOwner', { date });
  // Neutral, not success: an owner's attestation, not a check by Medvya (MDV-P2-060).
  return <Tag tone={d.expired ? 'danger' : 'neutral'} icon={Icon} label={label} />;
}

/** Doctors (F-09, MDV-P1-007): the doctors of the practice with their licence state, and the Add doctor wizard. */
export default function DoctorsPanel(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.loadFailed) {
    return (
      <div className="flex flex-col items-start gap-3">
        <p role="alert" className={fieldError}>{t(p.forbidden ? 'settings.forbidden' : 'settings.loadFailed')}</p>
        {!p.forbidden && <Button type="button" variant="outline" onClick={p.onRetry}>{t('doctors.retry')}</Button>}
      </div>
    );
  }
  return (
    <div className="flex flex-col gap-4">
      {p.added && (
        <p role="status" className="flex items-start gap-2 rounded-md border bg-muted p-3">
          <Info className="mt-0.5 size-4 shrink-0" aria-hidden />
          <span>{t('doctors.added', { name: p.added.name })}</span>
          <Button type="button" variant="ghost" className="ms-auto" onClick={p.onDismissAdded}>{t('doctors.dismiss')}</Button>
        </p>
      )}
      {p.adding
        ? <DoctorWizard {...p} />
        : (
          <Section title={t('doctors.title')} intro={t('doctors.intro')}>
            <div><Button type="button" onClick={p.onOpen}>{t('doctors.add')}</Button></div>
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
      {!editing && <div><Button type="button" variant="outline" onClick={() => p.onCorrect(d)}>{t(d.expired ? 'doctors.licence.renew' : 'doctors.licence.correct')}</Button></div>}
      {editing && (
        <form noValidate onSubmit={p.onSubmitLicence} className={fieldGrid} aria-label={t('doctors.licence.correct')}>
          <StepErrorSummary errors={p.licenceErrors} fields={licenceFields} prefix={`c-${d.id}`} focusKey={p.licenceForm.formState.submitCount} />
          <LicenceFields form={p.licenceForm} errors={p.licenceErrors} authorities={p.authorities} prefix={`c-${d.id}`} />
          <div className="flex flex-wrap items-center gap-3 col-span-full" aria-live="polite">
            <Button type="submit" loading={p.correctingBusy}>{t('doctors.licence.resubmit')}</Button>
            <Button type="button" variant="ghost" onClick={() => p.onCorrect(null)}>{t('settings.cancel')}</Button>
            {p.correctFailed && <span role="alert" className={fieldError}>{t('settings.saveFailed')}</span>}
          </div>
        </form>
      )}
      {/* MDV-P1-111: the room at this location; the board, the waiting-room TV and the token slip show it. */}
      <form noValidate className="flex flex-wrap items-end gap-3" aria-live="polite"
        onSubmit={e => { e.preventDefault(); p.onRoom(d.id, String(new FormData(e.currentTarget).get('room') ?? '')); }}>
        <Field id={`room-${d.id}`} label={t('doctors.room.at', { location: d.facilityName })} className="min-w-60">
          <Input name="room" autoComplete="off" maxLength={50} defaultValue={d.room ?? ''} placeholder={t('settings.schedules.roomPlaceholder')} />
        </Field>
        <Button type="submit" variant="outline" loading={p.roomBusyId === d.id}>{t('doctors.room.save')}</Button>
        {p.roomSavedId === d.id && <span role="status">{t('settings.saved')}</span>}
        {p.roomFailedId === d.id && <span role="alert" className={fieldError}>{t('settings.saveFailed')}</span>}
      </form>
      <SignatureImage id={d.id} hasSignature={d.hasSignature} own={d.ownSignature} problem={problem} busy={busy}
        onChoose={file => p.onSignature(d.id, file)} onRemove={() => p.onRemoveSignature(d.id)} />
    </li>
  );
}
