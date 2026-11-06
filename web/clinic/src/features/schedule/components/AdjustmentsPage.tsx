import type { UseFormReturn } from 'react-hook-form';
import { Link } from 'react-router';
import { ArrowLeft, CircleAlert, CircleCheck } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, cn, Input, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Textarea } from '@medvya/ui';
import { adjustmentKinds, type AdjustmentForm, rebookActions, type AdjustmentError, type RebookAction, type SaveResult, type SavedAdjustment } from '../domain/adjustments';
import { localDate } from '../domain/changes';
import type { Appointment } from '../domain/schedule';

type Props = {
  form: UseFormReturn<AdjustmentForm>;
  calendars: { id: string; name: string }[];
  timeZone: string | null;
  loading: boolean;
  saved: SavedAdjustment[];
  editingId: string | null;
  onEdit: (row: SavedAdjustment) => void;
  onCancelEdit: () => void;
  /** The saved row waiting for the desk to confirm its deletion. */
  confirmingId: string | null;
  onAskDelete: (id: string | null) => void;
  onDelete: (id: string) => void;
  deleting: boolean;
  deleteError: AdjustmentError | null;
  /** The booked visits the change leaves without a doctor, once the server has listed them; null before. */
  affected: Appointment[] | null;
  chosen: Record<string, RebookAction>;
  onChoose: (appointmentId: string, action: RebookAction) => void;
  canSave: boolean;
  saving: boolean;
  onSave: (e?: React.BaseSyntheticEvent) => Promise<void>;
  result: SaveResult | null;
  error: AdjustmentError | null;
  backPath: string;
  boardPath: string | null;
};

const hhmm = (time: string | null) => time?.slice(0, 5) ?? '';

/** P-04 "Leave and exceptions" (MDV-P1-012): leave, an extra session or a shortened day; affected visits need an action each before it saves. */
export default function AdjustmentsPage(p: Props) {
  const { t, formatDay } = useI18n();
  const { register, watch, setValue, formState: { errors } } = p.form;
  const kind = watch('kind');
  const calendarId = watch('calendarId');
  const zone = p.timeZone ?? 'UTC';
  const fieldError = (m: 'calendar' | 'range' | 'window' | 'tooLong') => t(`adjustment.error.${m}`);
  const control = 'min-h-11';

  return (
    <div className="flex max-w-[760px] flex-col gap-5">
      <Link to={p.backPath} className={cn(buttonVariants({ variant: 'ghost' }), 'self-start px-2')}>
        <ArrowLeft className="rtl:rotate-180" aria-hidden />{t('adjustment.back')}
      </Link>
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-extrabold">{t('adjustment.title')}</h1>
        <p className="text-muted-foreground">{t('adjustment.intro')}</p>
      </div>

      {p.result && (
        <div role="status" className="flex flex-col items-start gap-2 rounded-xl border bg-card p-4">
          <p className="flex items-center gap-2 font-bold"><CircleCheck className="size-5 text-success-700" aria-hidden />{t('adjustment.saved')}</p>
          {p.result.offered + p.result.manual > 0 && <p>{t('adjustment.savedCounts', { offered: p.result.offered, manual: p.result.manual })}</p>}
          {p.boardPath && p.result.offered + p.result.manual > 0 && <Link to={p.boardPath} className={buttonVariants({ variant: 'link' })}>{t('adjustment.toBoard')}</Link>}
        </div>
      )}

      <form noValidate onSubmit={e => { void p.onSave(e); }} className="flex flex-col gap-5 rounded-xl border bg-card p-4">
        <div className="grid gap-4 md:grid-cols-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="adj-calendar">{t('adjustment.calendar')}</Label>
            <Select value={calendarId ?? ''} onValueChange={v => setValue('calendarId', v, { shouldValidate: true })} disabled={p.loading || !!p.editingId}>
              <SelectTrigger id="adj-calendar" className={cn(control, 'w-full')} aria-invalid={!!errors.calendarId}><SelectValue placeholder={t('adjustment.calendar.choose')} /></SelectTrigger>
              <SelectContent>{p.calendars.map(c => <SelectItem key={c.id} value={c.id}>{c.name || t('schedule.doctor')}</SelectItem>)}</SelectContent>
            </Select>
            {errors.calendarId && <p role="alert" className="text-sm font-bold text-destructive">{fieldError('calendar')}</p>}
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="adj-kind">{t('adjustment.kind')}</Label>
            <Select value={kind} onValueChange={v => setValue('kind', v as typeof kind, { shouldValidate: true })}>
              <SelectTrigger id="adj-kind" className={cn(control, 'w-full')}><SelectValue /></SelectTrigger>
              <SelectContent>{adjustmentKinds.map(k => <SelectItem key={k} value={k}>{t(`adjustment.kind.${k}`)}</SelectItem>)}</SelectContent>
            </Select>
          </div>
        </div>

        <div className="grid gap-4 md:grid-cols-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="adj-from">{kind === 'Leave' ? t('adjustment.from') : t('adjustment.date')}</Label>
            <Input id="adj-from" type="date" className={control} aria-invalid={!!errors.fromDate} {...register('fromDate')} />
            {errors.fromDate && <p role="alert" className="text-sm font-bold text-destructive">{fieldError('range')}</p>}
          </div>
          {kind === 'Leave' && (
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="adj-to">{t('adjustment.to')}</Label>
              <Input id="adj-to" type="date" className={control} aria-invalid={!!errors.toDate} {...register('toDate')} />
              {errors.toDate && <p role="alert" className="text-sm font-bold text-destructive">{fieldError('range')}</p>}
            </div>
          )}
        </div>

        {kind !== 'Leave' && (
          <div className="grid gap-4 md:grid-cols-2">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="adj-start">{t('adjustment.start')}</Label>
              <Input id="adj-start" type="time" step={300} className={control} aria-invalid={!!errors.start} {...register('start')} />
            </div>
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="adj-end">{t('adjustment.end')}</Label>
              <Input id="adj-end" type="time" step={300} className={control} {...register('end')} />
            </div>
            {errors.start && <p role="alert" className="text-sm font-bold text-destructive md:col-span-2">{fieldError('window')}</p>}
          </div>
        )}

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="adj-reason">{t('adjustment.reason')}</Label>
          <Textarea id="adj-reason" rows={2} className="field-sizing-fixed" aria-describedby="adj-reason-hint" aria-invalid={!!errors.reason} {...register('reason')} />
          <p id="adj-reason-hint" className="text-xs text-muted-foreground">{t('adjustment.reasonHint')}</p>
          {errors.reason && <p role="alert" className="text-sm font-bold text-destructive">{fieldError('tooLong')}</p>}
        </div>

        {p.affected && p.affected.length > 0 && <Affected visits={p.affected} chosen={p.chosen} zone={zone} onChoose={p.onChoose} />}

        <div aria-live="polite">
          {p.error && (
            <p role="alert" className="flex items-start gap-1.5 text-sm font-semibold text-danger-700">
              <CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />{t(`adjustment.error.${p.error}`)}
            </p>
          )}
        </div>
        {p.editingId && <p className="text-sm font-bold">{t('adjustment.editing')}</p>}
        <div className="flex flex-wrap gap-2">
          <Button type="submit" disabled={!p.canSave} loading={p.saving}>{t('adjustment.save')}</Button>
          {p.editingId && <Button type="button" variant="outline" onClick={p.onCancelEdit}>{t('adjustment.cancelEdit')}</Button>}
        </div>
      </form>

      <section aria-labelledby="adj-saved" className="flex flex-col gap-2">
        <h2 id="adj-saved" className="text-lg font-extrabold">{t('adjustment.upcoming')}</h2>
        <p className="text-sm text-muted-foreground">{t('adjustment.deleteNote')}</p>
        <div aria-live="polite">
          {p.deleteError && <p role="alert" className="text-sm font-semibold text-danger-700">{t(`adjustment.error.${p.deleteError}`)}</p>}
        </div>
        {p.saved.length === 0 ? <p className="text-muted-foreground">{t('adjustment.none')}</p> : (
          <ul className="flex flex-col gap-2">
            {p.saved.map(s => {
              const name = p.calendars.find(c => c.id === s.calendarId)?.name ?? '';
              return (
                <li key={s.id} className="flex flex-col gap-2 rounded-lg border bg-card p-3">
                  <Saved row={s} name={name} formatDay={formatDay} />
                  {p.confirmingId === s.id ? (
                    <div className="flex flex-col items-start gap-2">
                      <p className="text-sm font-bold">{t('adjustment.delete.confirm')}</p>
                      <div className="flex flex-wrap gap-2">
                        <Button type="button" variant="destructive" loading={p.deleting} onClick={() => p.onDelete(s.id)}>{t('adjustment.delete.yes')}</Button>
                        <Button type="button" variant="outline" onClick={() => p.onAskDelete(null)}>{t('adjustment.delete.no')}</Button>
                      </div>
                    </div>
                  ) : (
                    <div className="flex flex-wrap gap-2">
                      <Button type="button" variant="outline" onClick={() => p.onEdit(s)} aria-label={t('adjustment.edit.named', { name })}>{t('adjustment.edit')}</Button>
                      <Button type="button" variant="outline" onClick={() => p.onAskDelete(s.id)} aria-label={t('adjustment.delete.named', { name })}>{t('adjustment.delete')}</Button>
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </section>
    </div>
  );
}

function Saved({ row, name, formatDay }: { row: SavedAdjustment; name: string; formatDay: (d: string) => string }) {
  const { t } = useI18n();
  const dates = row.fromDate === row.toDate ? formatDay(row.fromDate) : t('adjustment.range', { from: formatDay(row.fromDate), to: formatDay(row.toDate) });
  return (
    <p>
      <span className="font-bold">{name}</span>{' · '}{t(`adjustment.kind.${row.kind}`)}{' · '}{dates}
      {row.start && row.end ? ` · ${t('adjustment.window', { start: hhmm(row.start), end: hhmm(row.end) })}` : ''}
      {row.reason ? ` · ${row.reason}` : ''}
    </p>
  );
}

/** The visits the change leaves without a doctor: an action for each, in words (never colour alone). */
function Affected({ visits, chosen, zone, onChoose }: { visits: Appointment[]; chosen: Record<string, RebookAction>; zone: string; onChoose: (id: string, a: RebookAction) => void }) {
  const { t, formatDay, formatTime } = useI18n();
  return (
    <section aria-labelledby="adj-affected" className="flex flex-col gap-3 rounded-lg border border-dashed p-3">
      <h2 id="adj-affected" className="text-lg font-extrabold">{t('adjustment.affected.title', { count: visits.length })}</h2>
      <p className="text-sm text-muted-foreground">{t('adjustment.affected.body')}</p>
      <ul className="flex flex-col gap-3">
        {visits.map(v => (
          <li key={v.id}>
            <fieldset className="flex flex-col gap-1">
              <legend className="font-bold">{t('adjustment.affected.visit', { name: v.patientName, day: formatDay(localDate(v.start, zone)), time: formatTime(new Date(v.start), zone) })}</legend>
              {rebookActions.map(a => (
                <label key={a} className="flex min-h-11 items-center gap-2">
                  <input type="radio" name={`action-${v.id}`} className="size-5" checked={chosen[v.id] === a} onChange={() => onChoose(v.id, a)} />
                  {t(`adjustment.action.${a}`)}
                </label>
              ))}
            </fieldset>
          </li>
        ))}
      </ul>
    </section>
  );
}
