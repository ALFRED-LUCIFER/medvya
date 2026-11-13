import { Link } from 'react-router';
import type { UseFormReturn } from 'react-hook-form';
import { ArrowLeft, CalendarClock, CircleAlert, CircleCheck, Clock, Send } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Card, cn, Input, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, PageContainer, PageHeader } from '@medvya/ui';
import PatientsMessage from '../../patients/components/PatientsMessage';
import { PatientPicker, type PatientSearchView } from '../../schedule/components/BookingPanel';
import { reasonCategories, visitTypes, type PatientMatch } from '../../schedule/domain/booking';
import { termsPath } from '../../settings/domain/settings';
import { offerable, weekDays, type DayName, type Outcome, type SlotMatch, type WaitlistEntry, type WaitlistError, type WaitlistForm } from '../domain/waitlist';

type Props = {
  form: UseFormReturn<WaitlistForm>;
  patientSearch: PatientSearchView;
  patientId: string;
  patient: { loading: boolean; match: PatientMatch | null } | null;
  doctors: { id: string; name: string }[];
  entries: WaitlistEntry[];
  timeZone: string | null;
  loading: boolean;
  failedToLoad: boolean;
  forbidden: boolean;
  canAdd: boolean;
  adding: boolean;
  onAdd: () => void;
  pendingId: string | null;
  onOffer: (e: WaitlistEntry) => void;
  onAccept: (e: WaitlistEntry) => void;
  onDecline: (e: WaitlistEntry) => void;
  onRemove: (e: WaitlistEntry) => void;
  outcome: Outcome | null;
  error: WaitlistError | null;
  boardPath: string;
};

const anyDoctor = 'any';

/** P-05 Waitlist (MDV-P1-027): who wants a slot, the free slot that suits each, "Offer {time}", and the patient's answer. */
export default function WaitlistPage(p: Props) {
  const { t, formatTime, formatDate, formatDay } = useI18n();
  if (p.forbidden) return <PatientsMessage pageHeading title={t('err.403')} body={t('waitlist.forbidden')} />;
  const zone = p.timeZone ?? 'UTC';
  const when = (start: string) => t('appointment.messages.at', { day: formatDate(new Date(start), zone), time: formatTime(new Date(start), zone) });
  const slot = (s: SlotMatch) => t('waitlist.slot', { when: when(s.start), doctor: s.calendarName });
  const { register, watch, setValue, formState: { errors } } = p.form;
  const days = watch('days');
  const toggleDay = (d: DayName) => setValue('days', days.includes(d) ? days.filter(x => x !== d) : [...days, d], { shouldDirty: true });

  return (
    <PageContainer width="reading" className="gap-5">
      <PageHeader title={t('waitlist.title')} description={t('waitlist.intro')}
        back={<Link to={p.boardPath} className={cn(buttonVariants({ variant: 'ghost' }), 'px-2')}><ArrowLeft className="rtl:rotate-180" aria-hidden />{t('walkIn.back')}</Link>} />

      <div aria-live="polite">
        {p.outcome && (
          <Card size="item" role="status">
            <p className="flex items-start gap-1.5 text-sm font-semibold">
              <CircleCheck className="mt-0.5 size-4 shrink-0 text-success-700" aria-hidden />
              {p.outcome.kind === 'booked' ? t('waitlist.outcome.booked', { name: p.outcome.visit.patientName, when: when(p.outcome.visit.start) }) : t(`waitlist.outcome.${p.outcome.kind}`)}
            </p>
          </Card>
        )}
        {p.error && (
          <p role="alert" className="flex items-start gap-1.5 text-sm font-semibold text-danger-700">
            <CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />{t(`waitlist.error.${p.error}`)}
            {p.error === 'terms_not_accepted' && <Link to={termsPath} className={cn(buttonVariants({ variant: 'link' }), 'h-auto px-0')}>{t('terms.open')}</Link>}
          </p>
        )}
      </div>

      <section aria-labelledby="waitlist-list" className="flex flex-col gap-3">
        <h2 id="waitlist-list" className="text-lg font-bold">{t('waitlist.list')}</h2>
        {p.loading && <p className="text-muted-foreground">{t('patients.loading')}</p>}
        {p.failedToLoad && <p role="alert" className="font-bold text-destructive">{t('waitlist.loadFailed')}</p>}
        {!p.loading && !p.failedToLoad && p.entries.length === 0 && <p className="text-muted-foreground">{t('waitlist.empty')}</p>}
        <ul className="flex flex-col gap-3">
          {p.entries.map(e => {
            const busy = p.pendingId === e.id;
            return (
              <li key={e.id} data-testid="waitlist-entry"><Card size="item">
                <div className="flex flex-wrap items-baseline justify-between gap-2">
                  <p className="font-bold">{t('waitlist.patient', { name: e.patientName, mrn: e.mrn })}</p>
                  {/* State in words and an icon, never colour alone. */}
                  <p className="inline-flex items-center gap-1.5 text-sm font-bold">
                    {e.state === 'Offered' ? <Send className="size-4" aria-hidden /> : <Clock className="size-4" aria-hidden />}
                    {t(`waitlist.state.${e.state}`)}
                  </p>
                </div>
                <p className="text-sm text-muted-foreground">
                  {[e.calendarName ?? t('waitlist.anyDoctor'), t(`visitType.${e.visitType}`),
                    e.days.length ? weekDays.filter(d => e.days.includes(d)).map(d => t(`waitlist.day.${d}`)).join(', ') : t('waitlist.anyDay'),
                    e.from || e.until ? t('waitlist.window', { from: e.from?.slice(0, 5) ?? '…', until: e.until?.slice(0, 5) ?? '…' }) : t('waitlist.anyTime')].join(' · ')}
                </p>
                {e.dueOn && <p className="inline-flex items-center gap-1.5 text-sm font-semibold"><CalendarClock className="size-4" aria-hidden />{t('waitlist.followUp', { day: formatDay(e.dueOn) })}</p>}
                <p className="text-xs text-muted-foreground">{t('waitlist.requested', { when: when(e.requestedAt) })}</p>
                {e.offered && e.offerExpiresAt && <p className="text-sm font-semibold">{t('waitlist.heldUntil', { slot: slot(e.offered), until: formatTime(new Date(e.offerExpiresAt), zone) })}</p>}
                {e.state === 'Waiting' && !e.match && <p className="text-sm text-muted-foreground">{t('waitlist.noMatch')}</p>}
                <div className="flex flex-wrap gap-2">
                  {offerable(e) && <Button type="button" loading={busy} onClick={() => p.onOffer(e)}><Send aria-hidden />{t('waitlist.offer', { when: when(e.match!.start) })}</Button>}
                  {e.state === 'Offered' && <>
                    <Button type="button" loading={busy} onClick={() => p.onAccept(e)}><CircleCheck aria-hidden />{t('waitlist.accept')}</Button>
                    <Button type="button" variant="outline" disabled={busy} onClick={() => p.onDecline(e)}>{t('waitlist.decline')}</Button>
                  </>}
                  <Button type="button" variant="ghost" disabled={busy} onClick={() => p.onRemove(e)}>{t('waitlist.remove')}</Button>
                </div>
                {offerable(e) && <p className="text-xs text-muted-foreground">{slot(e.match!)}</p>}
              </Card></li>
            );
          })}
        </ul>
      </section>

      <Card><form noValidate onSubmit={e => { e.preventDefault(); p.onAdd(); }} className="flex flex-col gap-4" aria-labelledby="waitlist-add">
        <h2 id="waitlist-add" className="text-lg font-bold">{t('waitlist.add')}</h2>
        <PatientPicker search={p.patientSearch} chosen={p.patientId} registerPath={null} invalid={false} />
        {p.patient?.loading && <p className="text-sm text-muted-foreground">{t('patients.loading')}</p>}
        {p.patient?.match && <p className="font-bold" role="status">{t('walkIn.patient', { name: p.patient.match.fullName, mrn: p.patient.match.mrn })}</p>}

        <div className="grid gap-4 md:grid-cols-3">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="waitlist-doctor">{t('walkIn.doctor')}</Label>
            <Select value={watch('calendarId') || anyDoctor} onValueChange={v => setValue('calendarId', v === anyDoctor ? '' : v)}>
              <SelectTrigger id="waitlist-doctor" className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value={anyDoctor}>{t('waitlist.anyDoctor')}</SelectItem>
                {p.doctors.map(d => <SelectItem key={d.id} value={d.id}>{d.name}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="waitlist-type">{t('booking.visitType')}</Label>
            <Select value={watch('visitType')} onValueChange={v => setValue('visitType', v as WaitlistForm['visitType'])}>
              <SelectTrigger id="waitlist-type" className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>{visitTypes.map(v => <SelectItem key={v} value={v}>{t(`visitType.${v}`)}</SelectItem>)}</SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="waitlist-reason">{t('booking.reasonCategory')}</Label>
            <Select value={watch('reasonCategory')} onValueChange={v => setValue('reasonCategory', v as WaitlistForm['reasonCategory'])}>
              <SelectTrigger id="waitlist-reason" className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>{reasonCategories.map(r => <SelectItem key={r} value={r}>{t(`reasonCategory.${r}`)}</SelectItem>)}</SelectContent>
            </Select>
          </div>
        </div>

        <fieldset className="flex flex-col gap-1.5">
          <legend className="mb-1.5 text-sm font-medium">{t('waitlist.days')}</legend>
          <div className="flex flex-wrap gap-2">
            {weekDays.map(d => (
              <label key={d} className="inline-flex min-h-11 cursor-pointer items-center gap-2 rounded-md border px-3 text-sm has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-ring">
                <input type="checkbox" className="size-4" checked={days.includes(d)} onChange={() => toggleDay(d)} />{t(`waitlist.day.${d}`)}
              </label>
            ))}
          </div>
          <p className="text-xs text-muted-foreground">{t('waitlist.daysHint')}</p>
        </fieldset>

        <div className="flex flex-wrap gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="waitlist-from">{t('waitlist.from')}</Label>
            <Input id="waitlist-from" type="time" step={300} dir="ltr" className="w-32" {...register('from')} />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="waitlist-until">{t('waitlist.until')}</Label>
            <Input id="waitlist-until" type="time" step={300} dir="ltr" className="w-32" aria-invalid={!!errors.until} aria-describedby={errors.until ? 'waitlist-until-error' : undefined} {...register('until')} />
            {errors.until && <p id="waitlist-until-error" className="text-sm font-bold text-destructive">{t('waitlist.error.before_from')}</p>}
          </div>
        </div>
        <Button type="submit" className="self-start" disabled={!p.canAdd} loading={p.adding}>{t('waitlist.addSubmit')}</Button>
      </form></Card>
    </PageContainer>
  );
}
