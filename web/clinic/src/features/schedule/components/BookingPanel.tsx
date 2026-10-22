import type { UseFormReturn } from 'react-hook-form';
import { Link } from 'react-router';
import { CircleAlert, CircleCheck, Hourglass, Search } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, cn, Input, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Textarea } from '@medvya/ui';
import PatientSummaryLine from '../../patients/components/PatientSummaryLine';
import type { SearchErrorCode, SearchResult } from '../../patients/domain/directory';
import { channels, reasonCategories, visitTypes, type BookedAppointment, type BookingForm, type Hold, type PatientMatch, type SlotChoice, type SlotError } from '../domain/booking';
import type { VisitLength, VisitType } from '../domain/schedule';

type Props = {
  timeZone: string;
  /** Lengths of each visit type in the chosen calendar, to label the visit type choices. */
  visitLengths: VisitLength[];
  choice: SlotChoice | null;
  holding: boolean;
  hold: Hold | null;
  holdError: SlotError | null;
  form: UseFormReturn<BookingForm>;
  reasonCategory: BookingForm['reasonCategory'];
  channel: BookingForm['channel'];
  onReasonCategory: (v: BookingForm['reasonCategory']) => void;
  onChannel: (v: BookingForm['channel']) => void;
  patient: { loading: boolean; match: PatientMatch | null } | null;
  chosenPatientId: string;
  /** Front-desk search (MDV-P1-018) to choose the patient; rows show initials, age, sex and MRN tail only. */
  patientSearch: {
    text: string; onText: (text: string) => void; onSubmit: () => void;
    state: 'idle' | 'loading' | 'ready' | 'invalid' | 'forbidden' | 'notFound' | 'error';
    result: SearchResult | null; errorCode: SearchErrorCode | null; onPick: (id: string) => void;
  };
  /** Where to register a new patient, when this membership may. */
  registerPath: string | null;
  booking: boolean;
  bookError: SlotError | null;
  booked: BookedAppointment | null;
  onVisitType: (v: VisitType) => void;
  onConfirm: () => void;
  onClose: () => void;
  onDismissBooked: () => void;
};

const field = 'flex flex-col gap-1.5';
const control = 'h-11 w-full';

/** P-04 booking panel: confirm the patient, visit type, nonclinical reason category and channel for the held slot. */
export default function BookingPanel(p: Props) {
  const { t, formatTime } = useI18n();
  const time = (iso: string) => formatTime(new Date(iso), p.timeZone);

  if (!p.choice)
    return (
      <aside aria-label={t('booking.title')} className="flex flex-col gap-3 rounded-xl border bg-card p-4">
        {p.booked ? (
          <div role="status" className="flex flex-col items-start gap-2">
            <p className="flex items-center gap-2 font-bold text-success-700"><CircleCheck aria-hidden className="size-5" />{t('booking.done.title')}</p>
            <p className="text-sm">{t('booking.done.body', { name: p.booked.patientName, time: time(p.booked.start), reference: p.booked.reference })}</p>
            <Button variant="outline" onClick={p.onDismissBooked}>{t('booking.done.dismiss')}</Button>
          </div>
        ) : (
          <>
            <h2 className="text-base font-extrabold">{t('booking.hint.title')}</h2>
            <p className="text-sm text-muted-foreground">{t('booking.hint.body')}</p>
          </>
        )}
      </aside>
    );

  const { register, formState: { errors } } = p.form;
  const lengthOf = (v: VisitType) => p.visitLengths.find(l => l.visitType === v)?.minutes;
  const end = p.hold?.end;
  return (
    <aside aria-label={t('booking.title')} className="rounded-xl border bg-card p-4">
      <form noValidate className="flex flex-col gap-4" onSubmit={e => { e.preventDefault(); p.onConfirm(); }}>
        <div>
          <h2 className="text-lg font-extrabold">{t('booking.title')}</h2>
          <p className="text-sm">{t('booking.when', { name: p.choice.calendarName, from: time(p.choice.start), to: end ? time(end) : '…' })}</p>
          <p role="status" className="mt-1 flex items-center gap-1.5 text-[13px] text-muted-foreground">
            <Hourglass aria-hidden className="size-4" />
            {p.holding ? t('booking.holding') : p.hold ? t('booking.heldUntil', { time: time(p.hold.expiresAt) }) : ''}
          </p>
        </div>
        {p.holdError && <p role="alert" className="rounded-md border border-destructive p-3 text-sm font-bold text-destructive">{t(`booking.error.${p.holdError}`)}</p>}

        <div className={field}>
          <Label htmlFor="booking-visit-type">{t('booking.visitType')}</Label>
          <Select value={p.choice.visitType} onValueChange={v => p.onVisitType(v as VisitType)}>
            <SelectTrigger id="booking-visit-type" className={`${control} data-[size=default]:h-11`}><SelectValue /></SelectTrigger>
            <SelectContent>
              {visitTypes.map(v => (
                <SelectItem key={v} value={v}>{t('booking.visitTypeLength', { type: t(`visitType.${v}`), minutes: lengthOf(v) ?? 0 })}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <PatientPicker search={p.patientSearch} chosen={p.chosenPatientId} registerPath={p.registerPath} invalid={!!errors.patientId} />

        <div aria-live="polite" className="text-sm font-bold">
          {p.patient && (p.patient.loading ? t('booking.patientLooking')
            : p.patient.match ? t('booking.patientFound', { name: p.patient.match.fullName, mrn: p.patient.match.mrn }) : t('booking.patientMissing'))}
        </div>

        <div className={field}>
          <Label htmlFor="booking-reason">{t('booking.reasonCategory')}</Label>
          <Select value={p.reasonCategory} onValueChange={v => p.onReasonCategory(v as BookingForm['reasonCategory'])}>
            <SelectTrigger id="booking-reason" className={`${control} data-[size=default]:h-11`}><SelectValue /></SelectTrigger>
            <SelectContent>{reasonCategories.map(r => <SelectItem key={r} value={r}>{t(`reasonCategory.${r}`)}</SelectItem>)}</SelectContent>
          </Select>
        </div>

        <div className={field}>
          <Label htmlFor="booking-reason-text">{t('booking.reasonText')}</Label>
          <Textarea id="booking-reason-text" rows={3} className="field-sizing-fixed" aria-invalid={!!errors.reasonText} {...register('reasonText')}
            aria-describedby={errors.reasonText ? 'booking-reason-error booking-reason-hint' : 'booking-reason-hint'} />
          <p id="booking-reason-hint" className="text-xs text-muted-foreground">{t('booking.reasonTextHint')}</p>
          {errors.reasonText && <p id="booking-reason-error" className="text-sm font-bold text-destructive">{t('booking.error.reasonText')}</p>}
        </div>

        <div className={field}>
          <Label htmlFor="booking-channel">{t('booking.channel')}</Label>
          <Select value={p.channel} onValueChange={v => p.onChannel(v as BookingForm['channel'])}>
            <SelectTrigger id="booking-channel" className={`${control} data-[size=default]:h-11`}><SelectValue /></SelectTrigger>
            <SelectContent>{channels.map(c => <SelectItem key={c} value={c}>{t(`channel.${c}`)}</SelectItem>)}</SelectContent>
          </Select>
        </div>

        {p.bookError && <p role="alert" className="rounded-md border border-destructive p-3 text-sm font-bold text-destructive">{t(`booking.error.${p.bookError}`)}</p>}
        <div className="flex flex-wrap gap-2">
          <Button type="submit" loading={p.booking} disabled={!p.hold || p.holding || !p.patient?.match}>{t('booking.confirm')}</Button>
          <Button type="button" variant="outline" onClick={p.onClose}>{t('booking.cancel')}</Button>
        </div>
      </form>
    </aside>
  );
}

type PickerProps = { search: Props['patientSearch']; chosen: string; registerPath: string | null; invalid: boolean };

/**
 * Choose the patient with front-desk search (MDV-P1-018). Rows show initials, age, sex and MRN tail only; the name and MRN
 * appear once one is chosen (an audited lookup). Inside the booking form, so Enter searches instead of submitting the booking.
 */
function PatientPicker({ search: s, chosen, registerPath, invalid }: PickerProps) {
  const { t } = useI18n();
  const hint = 'booking-patient-hint', searchError = 'booking-patient-search-error', pickError = 'booking-patient-error';
  const describedBy = [invalid && pickError, s.state === 'invalid' && searchError, hint].filter(Boolean).join(' ');
  return (
    <fieldset className={field}>
      <legend className="mb-1.5 text-sm font-medium">{t('booking.patient')}</legend>
      <Label htmlFor="booking-patient-search" className="sr-only">{t('patients.search.label')}</Label>
      <div className="flex gap-2">
        <Input id="booking-patient-search" type="search" autoComplete="off" spellCheck={false} maxLength={100} className={cn(control, 'min-w-0 flex-1')}
          placeholder={t('patients.search.label')} value={s.text} onChange={e => s.onText(e.target.value)} aria-describedby={describedBy}
          aria-invalid={invalid || s.state === 'invalid' || undefined}
          onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); s.onSubmit(); } }} />
        <Button type="button" variant="outline" onClick={s.onSubmit}><Search aria-hidden />{t('patients.search.submit')}</Button>
      </div>
      <p id={hint} className="text-xs text-muted-foreground">{t('patients.search.hint')}</p>
      {s.state === 'invalid' && (
        <p id={searchError} className="flex items-center gap-1.5 text-sm font-semibold text-danger-700">
          <CircleAlert className="size-4 shrink-0" aria-hidden />{t(s.errorCode ? `patients.invalid.${s.errorCode}` : 'patients.invalid.generic')}
        </p>
      )}
      {invalid && <p id={pickError} className="text-sm font-bold text-destructive">{t('booking.error.patientId')}</p>}
      <div aria-live="polite">
        {s.state === 'loading' && <p className="text-sm text-muted-foreground">{t('patients.loading')}</p>}
        {(s.state === 'error' || s.state === 'notFound') && <p className="text-sm font-bold text-destructive">{t('patients.error.title')}</p>}
        {s.state === 'forbidden' && <p className="text-sm font-bold text-destructive">{t('patients.forbidden.body')}</p>}
        {s.state === 'ready' && s.result?.items.length === 0 && <p className="text-sm font-bold">{t('patients.empty')}</p>}
      </div>
      {s.state === 'ready' && s.result && s.result.items.length > 0 && (
        <ul aria-label={t('booking.patientResults')} className="flex max-h-64 flex-col divide-y overflow-y-auto rounded-md border">
          {s.result.items.map(row => {
            const age = t(row.ageEstimated ? 'patients.ageEstimated' : 'patients.age', { age: row.ageYears });
            const label = t('booking.patientPick', { initials: row.initials.split('').join(' '), age, sex: t(`patients.sex.${row.sexAtBirth}`), mrn: row.mrnTail }) + (row.status === 'Incomplete' ? t('patients.row.incomplete') : '');
            return (
              <li key={row.id}>
                <button type="button" aria-pressed={chosen === row.id} aria-label={label} onClick={() => s.onPick(row.id)}
                  className={cn('flex min-h-14 w-full items-center gap-3 px-3 py-2 text-start hover:bg-accent focus-visible:-outline-offset-2', chosen === row.id && 'bg-accent')}>
                  <PatientSummaryLine row={row} />
                  {chosen === row.id && <CircleCheck aria-hidden className="size-5 shrink-0 text-success-700" />}
                </button>
              </li>
            );
          })}
        </ul>
      )}
      {registerPath && (
        <Link to={registerPath} className={cn(buttonVariants({ variant: 'link' }), 'self-start px-0')}>{t('patients.empty.register')}</Link>
      )}
    </fieldset>
  );
}
