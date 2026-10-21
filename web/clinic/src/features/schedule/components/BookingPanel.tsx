import type { UseFormReturn } from 'react-hook-form';
import { CircleCheck, Hourglass } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Input, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Textarea } from '@medvya/ui';
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

        <div className={field}>
          <Label htmlFor="booking-patient">{t('booking.patient')}</Label>
          <Input id="booking-patient" className={control} autoComplete="off" spellCheck={false} aria-invalid={!!errors.patientId}
            aria-describedby="booking-patient-hint" {...register('patientId')} />
          <p id="booking-patient-hint" className="text-xs text-muted-foreground">{t('booking.patientHint')}</p>
          {errors.patientId && <p className="text-sm font-bold text-destructive">{t('booking.error.patientId')}</p>}
          {p.patient && (
            <p role="status" className="text-sm font-bold">
              {p.patient.loading ? t('booking.patientLooking') : p.patient.match ? t('booking.patientFound', { name: p.patient.match.fullName, mrn: p.patient.match.mrn }) : t('booking.patientMissing')}
            </p>
          )}
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
          <Textarea id="booking-reason-text" rows={3} aria-describedby="booking-reason-hint" aria-invalid={!!errors.reasonText} {...register('reasonText')} />
          <p id="booking-reason-hint" className="text-xs text-muted-foreground">{t('booking.reasonTextHint')}</p>
          {errors.reasonText && <p className="text-sm font-bold text-destructive">{t('booking.error.reasonText')}</p>}
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
