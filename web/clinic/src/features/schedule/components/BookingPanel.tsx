import type { UseFormReturn } from 'react-hook-form';
import { Link } from 'react-router';
import { CircleAlert, CircleCheck, Hourglass, Search } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, cn, Input, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Textarea } from '@medvya/ui';
import PatientSummaryLine from '../../patients/components/PatientSummaryLine';
import type { SearchErrorCode, SearchResult } from '../../patients/domain/directory';
import { channels, reasonCategories, visitTypes, type BookedAppointment, type BookingForm, type Hold, type PatientMatch, type SlotChoice, type SlotError } from '../domain/booking';
import type { VisitLength, VisitType } from '../domain/schedule';
import { localTime, weekdayOrder, type PlanRow, type SeriesDone } from '../domain/series';
import { slotError } from '../domain/booking';
import { localDate } from '../domain/changes';
import { termsPath } from '../../settings/domain/settings';

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
  /** MDV-P1-026: repeat weekly. `plan` is the reviewed series (null until previewed); the desk skips or moves conflicts before confirming. */
  repeat: number;
  /** The repeat controls are shown (the desk asked for them). */
  repeatOpen: boolean;
  onRepeatOpen: (open: boolean) => void;
  maxRepeat: number;
  onRepeat: (n: number) => void;
  unit: 'weekly' | 'monthly' | 'weekdays';
  onUnit: (unit: 'weekly' | 'monthly' | 'weekdays') => void;
  /** Chosen days of the week for the 'weekdays' pattern (0 = Sunday). */
  days: number[];
  onToggleDay: (day: number) => void;
  plan: PlanRow[] | null;
  seriesDone: SeriesDone | null;
  onToggleOccurrence: (index: number) => void;
  onMoveOccurrence: (index: number, to: { date?: string; time?: string }) => void;
  onBackToForm: () => void;
  onDismissSeries: () => void;
  onConfirm: () => void;
  onClose: () => void;
  onDismissBooked: () => void;
  /** Rebooking a no-show: the patient the next chosen slot is booked for. */
  rebook?: { name: string } | null;
};

const field = 'flex flex-col gap-1.5';
const control = 'h-11 w-full';

/** P-04 booking panel: confirm the patient, visit type, nonclinical reason category and channel for the held slot. */
export default function BookingPanel(p: Props) {
  const { t, formatTime, formatShortDay } = useI18n();
  const time = (iso: string) => formatTime(new Date(iso), p.timeZone);
  const day = (iso: string) => formatShortDay(localDate(iso, p.timeZone));

  if (!p.choice)
    return (
      <aside aria-label={t('booking.title')} className="flex flex-col gap-3 rounded-xl border bg-card p-4">
        {p.seriesDone ? (
          <div role="status" className="flex flex-col items-start gap-2">
            <p className="flex items-center gap-2 font-bold text-success-700"><CircleCheck aria-hidden className="size-5" />{t('series.done.title', { count: p.seriesDone.booked })}</p>
            {p.seriesDone.refused.length > 0 && (
              <>
                <p className="flex items-center gap-2 text-sm font-bold text-danger-700"><CircleAlert aria-hidden className="size-4 shrink-0" />{t('series.done.refused', { count: p.seriesDone.refused.length })}</p>
                <ul className="text-sm">
                  {p.seriesDone.refused.map(r => <li key={r.start}>{t('series.row.refused', { day: day(r.start), time: time(r.start), why: t(`booking.error.${slotError(r.conflict ?? undefined)}`) })}</li>)}
                </ul>
              </>
            )}
            <Button variant="outline" onClick={p.onDismissSeries}>{t('booking.done.dismiss')}</Button>
          </div>
        ) : p.booked ? (
          <div role="status" className="flex flex-col items-start gap-2">
            <p className="flex items-center gap-2 font-bold text-success-700"><CircleCheck aria-hidden className="size-5" />{t('booking.done.title')}</p>
            <p className="text-sm">{t('booking.done.body', { name: p.booked.patientName, time: time(p.booked.start), reference: p.booked.reference })}</p>
            <Button variant="outline" onClick={p.onDismissBooked}>{t('booking.done.dismiss')}</Button>
          </div>
        ) : (
          <>
            <h2 className="text-base font-extrabold">{p.rebook ? t('booking.rebook.title', { name: p.rebook.name }) : t('booking.hint.title')}</h2>
            <p className="text-sm text-muted-foreground">{t(p.rebook ? 'booking.rebook.body' : 'booking.hint.body')}</p>
          </>
        )}
      </aside>
    );

  const { register, formState: { errors } } = p.form;
  if (p.plan)
    return (
      <aside aria-label={t('booking.title')} className="flex flex-col gap-4 rounded-xl border bg-card p-4">
        <div>
          <h2 className="text-lg font-extrabold">{t('series.review.title')}</h2>
          <p className="text-sm text-muted-foreground">{t('series.review.body')}</p>
        </div>
        <ul className="flex flex-col divide-y rounded-md border">
          {p.plan.map((row, i) => (
            <li key={row.start} className="flex flex-col gap-2 p-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="text-sm font-bold">{t('series.row.when', { day: day(row.start), time: time(row.start) })}</span>
                <span className={cn('flex items-center gap-1.5 text-xs font-bold', row.conflict ? 'text-danger-700' : 'text-success-700')}>
                  {row.conflict ? <CircleAlert aria-hidden className="size-4 shrink-0" /> : <CircleCheck aria-hidden className="size-4 shrink-0" />}
                  {row.conflict ? t(`booking.error.${slotError(row.conflict)}`) : t(row.moved ? 'series.row.moved' : 'series.row.free')}
                </span>
              </div>
              <div className="flex flex-wrap items-end gap-3">
                <label className="flex min-h-11 items-center gap-2 text-sm">
                  <input type="checkbox" className="size-5" checked={row.include} onChange={() => p.onToggleOccurrence(i)} />
                  {t('series.row.include')}
                </label>
                {(row.conflict || row.moved) && (
                  <>
                    <div className="flex flex-col gap-1">
                      <Label htmlFor={`series-move-day-${i}`} className="text-xs">{t('series.row.moveToDay')}</Label>
                      <Input id={`series-move-day-${i}`} type="date" className="h-11 w-44" defaultValue={localDate(row.start, p.timeZone)} onBlur={e => { if (e.target.value && e.target.value !== localDate(row.start, p.timeZone)) p.onMoveOccurrence(i, { date: e.target.value }); }} />
                    </div>
                    <div className="flex flex-col gap-1">
                      <Label htmlFor={`series-move-${i}`} className="text-xs">{t('series.row.moveTo')}</Label>
                      <Input id={`series-move-${i}`} type="time" className="h-11 w-32" step={300} defaultValue={localTime(row.start, p.timeZone)} onBlur={e => { if (e.target.value && e.target.value !== localTime(row.start, p.timeZone)) p.onMoveOccurrence(i, { time: e.target.value }); }} />
                    </div>
                  </>
                )}
              </div>
            </li>
          ))}
        </ul>
        {p.bookError && <BookError code={p.bookError} />}
        <div className="flex flex-wrap gap-2">
          <Button type="button" loading={p.booking} disabled={!p.plan.some(r => r.include)} onClick={p.onConfirm}>{t('series.confirm', { count: p.plan.filter(r => r.include).length })}</Button>
          <Button type="button" variant="outline" onClick={p.onBackToForm}>{t('series.back')}</Button>
        </div>
      </aside>
    );
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
        {p.holdError && <BookError code={p.holdError} />}

        <div className={field}>
          <Label htmlFor="booking-visit-type">{t('booking.visitType')}</Label>
          <Select value={p.choice.visitType} onValueChange={v => p.onVisitType(v as VisitType)}>
            <SelectTrigger id="booking-visit-type" className={control}><SelectValue /></SelectTrigger>
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
            <SelectTrigger id="booking-reason" className={control}><SelectValue /></SelectTrigger>
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
            <SelectTrigger id="booking-channel" className={control}><SelectValue /></SelectTrigger>
            <SelectContent>{channels.map(c => <SelectItem key={c} value={c}>{t(`channel.${c}`)}</SelectItem>)}</SelectContent>
          </Select>
        </div>

        {/* One visit is the usual case: repeating stays folded until the desk asks for it (UX-10); folding it again books one visit. */}
        <details open={p.repeatOpen} onToggle={e => p.onRepeatOpen(e.currentTarget.open)} className="rounded-md border p-3">
          <summary className="flex min-h-11 cursor-pointer items-center text-sm font-bold">{t('series.toggle')}</summary>
          {p.repeatOpen && <div className="mt-2 flex flex-col gap-4">
            <div className={field}>
              <Label htmlFor="booking-unit">{t('series.unit')}</Label>
              <Select value={p.unit} onValueChange={v => p.onUnit(v as Props['unit'])}>
                <SelectTrigger id="booking-unit" className={control}><SelectValue /></SelectTrigger>
                <SelectContent>{(['weekly', 'monthly', 'weekdays'] as const).map(u => <SelectItem key={u} value={u}>{t(`series.unit.${u}`)}</SelectItem>)}</SelectContent>
              </Select>
            </div>

            {p.unit === 'weekdays' && (
              <fieldset className="flex flex-col gap-1">
                <legend className="text-sm font-bold">{t('series.weekdays')}</legend>
                <div className="flex flex-wrap gap-2">
                  {weekdayOrder.map(d => (
                    <label key={d} className="flex min-h-11 items-center gap-2 rounded-md border px-3 text-sm">
                      <input type="checkbox" className="size-5" checked={p.days.includes(d)} onChange={() => p.onToggleDay(d)} />
                      {t(`series.weekday.${d}`)}
                    </label>
                  ))}
                </div>
              </fieldset>
            )}

            <div className={field}>
              <Label htmlFor="booking-repeat">{t('series.repeat')}</Label>
              <Input id="booking-repeat" type="number" inputMode="numeric" min={1} max={p.maxRepeat} className={cn(control, 'w-28')} value={p.repeat}
                aria-describedby="booking-repeat-hint" onChange={e => p.onRepeat(e.target.valueAsNumber)} />
              <p id="booking-repeat-hint" className="text-xs text-muted-foreground">{t('series.repeatHint', { max: p.maxRepeat })}</p>
            </div>
          </div>}
        </details>

        {p.bookError && <BookError code={p.bookError} />}
        <div className="flex flex-wrap gap-2">
          <Button type="submit" loading={p.booking} disabled={!p.hold || p.holding || !p.patient?.match || (p.repeat > 1 && p.unit === 'weekdays' && p.days.length === 0)}>{t(p.repeat > 1 ? 'series.review.action' : 'booking.confirm')}</Button>
          <Button type="button" variant="outline" onClick={p.onClose}>{t('booking.cancel')}</Button>
        </div>
      </form>
    </aside>
  );
}

type PickerProps = { search: Props['patientSearch']; chosen: string; registerPath: string | null; invalid: boolean };
export type PatientSearchView = Props['patientSearch'];

/**
 * Choose the patient with front-desk search (MDV-P1-018). Rows show initials, age, sex and MRN tail only; the name and MRN
 * appear once one is chosen (an audited lookup). Inside the booking form, so Enter searches instead of submitting the booking.
 */
export function PatientPicker({ search: s, chosen, registerPath, invalid }: PickerProps) {
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
      {/* The link carries the slot: registration brings the desk back here with the new patient chosen. */}
      {registerPath && (
        <Link to={registerPath} className={cn(buttonVariants({ variant: 'link' }), 'self-start px-0')}>{t('patients.empty.register')}</Link>
      )}
    </fieldset>
  );
}

/** A refused booking; when the practice has not accepted the terms, the way to fix it is one tap away (MDV-P1-019). */
function BookError({ code }: { code: SlotError }) {
  const { t } = useI18n();
  return (
    <div role="alert" className="flex flex-col items-start gap-1 rounded-md border border-destructive p-3 text-sm font-bold text-destructive">
      {t(`booking.error.${code}`)}
      {code === 'terms_not_accepted' && <Link to={termsPath} className={cn(buttonVariants({ variant: 'link' }), 'px-0')}>{t('terms.open')}</Link>}
    </div>
  );
}
