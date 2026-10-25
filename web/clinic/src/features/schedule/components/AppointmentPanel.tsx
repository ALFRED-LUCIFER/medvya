import type { UseFormReturn } from 'react-hook-form';
import { CircleAlert, CircleCheck, X } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Textarea } from '@medvya/ui';
import { actions, cancelReasons, localDate, type Action, type CancelForm, type ChangeError, type Rule } from '../domain/changes';
import type { Appointment } from '../domain/schedule';
import { StateLabel } from './parts';

type Props = {
  timeZone: string;
  opened: { appointment: Appointment; calendarName: string };
  mode: 'view' | 'pick' | 'confirmMove' | 'cancel';
  target: { calendarName: string; start: string } | null;
  done: { kind: 'moved' | 'cancelled' | 'noShow' | 'checkedIn'; appointment: Appointment; calendarName: string } | null;
  rules: Record<Action, Rule>;
  working: boolean;
  error: ChangeError | null;
  form: UseFormReturn<CancelForm>;
  onClose: () => void;
  onReschedule: () => void;
  onConfirmMove: () => void;
  onStartCancel: () => void;
  onReason: (reason: CancelForm['reason']) => void;
  onConfirmCancel: () => void;
  onNoShow: () => void;
  onCheckIn: () => void;
  onBack: () => void;
  /** After a no-show: book the same patient again (opens booking with the patient filled in). */
  onRebook?: () => void;
};

const actionKeys: Record<Action, MessageKey> = {
  checkIn: 'appointment.action.checkIn', reschedule: 'appointment.action.reschedule', cancel: 'appointment.action.cancel', noShow: 'appointment.action.noShow',
};

/**
 * P-04 detail panel (MDV-P1-024): reference, patient, state, time, type and calendar, and the actions the state allows. A
 * disallowed action stays visible, disabled, with the reason beside it; the server refuses it too.
 */
export default function AppointmentPanel(p: Props) {
  const { t, formatShortDay, formatTime } = useI18n();
  const time = (iso: string) => formatTime(new Date(iso), p.timeZone);
  const day = (iso: string) => formatShortDay(localDate(iso, p.timeZone));
  const a = p.done?.appointment ?? p.opened.appointment;
  const minutes = Math.round((Date.parse(a.end) - Date.parse(a.start)) / 60_000);
  const run: Record<Action, () => void> = { checkIn: p.onCheckIn, reschedule: p.onReschedule, cancel: p.onStartCancel, noShow: p.onNoShow };

  return (
    <aside aria-label={t('appointment.title')} className="flex flex-col gap-4 rounded-xl border bg-card p-4">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="font-mono text-xs text-muted-foreground">{a.reference}</p>
          <h2 className="text-base font-extrabold break-words">{a.patientName}</h2>
          <p className="text-sm"><StateLabel state={a.state} /></p>
        </div>
        <Button variant="outline" size="icon" aria-label={t('appointment.close')} onClick={p.onClose}><X aria-hidden /></Button>
      </div>
      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-sm">
        <dt className="text-muted-foreground">{t('appointment.when')}</dt>
        <dd>{t('appointment.whenValue', { day: day(a.start), time: time(a.start), minutes })}</dd>
        <dt className="text-muted-foreground">{t('appointment.type')}</dt>
        <dd>{t(`visitType.${a.visitType}`)}</dd>
        <dt className="text-muted-foreground">{t('appointment.with')}</dt>
        <dd>{p.done?.calendarName ?? p.opened.calendarName}</dd>
      </dl>

      {p.done && (
        <div role="status" className="flex flex-col items-start gap-2">
          <p className="flex items-center gap-2 text-sm font-bold text-success-700"><CircleCheck aria-hidden className="size-5" />{t(`appointment.done.${p.done.kind}`, { time: time(a.start) })}</p>
          {p.done.kind === 'noShow' && p.onRebook && <Button variant="navy" onClick={p.onRebook}>{t('appointment.rebook', { name: a.patientName })}</Button>}
        </div>
      )}

      {p.error && (
        <p role="alert" className="flex items-start gap-2 text-sm font-bold text-danger-700">
          <CircleAlert aria-hidden className="mt-0.5 size-4 shrink-0" />{t(`appointment.error.${p.error}`)}
        </p>
      )}

      {!p.done && p.mode === 'view' && (
        <ul className="flex flex-col gap-2">
          {actions.map(k => {
            const rule = p.rules[k];
            const why = `appointment-why-${k}`;
            return (
              <li key={k} className="flex flex-col gap-1">
                <Button variant={k === 'cancel' || k === 'noShow' ? 'outline' : 'navy'} disabled={!rule.allowed || p.working} onClick={run[k]}
                  aria-describedby={rule.allowed ? undefined : why}>
                  {t(actionKeys[k])}
                </Button>
                {!rule.allowed && <p id={why} className="text-xs text-muted-foreground">{t(`appointment.why.${rule.why}`)}</p>}
              </li>
            );
          })}
        </ul>
      )}

      {p.mode === 'pick' && (
        <div role="status" className="flex flex-col items-start gap-2">
          <h3 className="text-sm font-extrabold">{t('appointment.pick.title')}</h3>
          <p className="text-sm text-muted-foreground">{t('appointment.pick.body', { name: a.patientName })}</p>
          <Button variant="outline" onClick={p.onBack}>{t('appointment.pick.back')}</Button>
        </div>
      )}

      {p.mode === 'confirmMove' && p.target && (
        <div role="group" aria-label={t('appointment.move.title')} className="flex flex-col items-start gap-2 rounded-md border border-action-600 p-3">
          <h3 className="text-sm font-extrabold">{t('appointment.move.title')}</h3>
          <p className="text-sm">{t('appointment.move.body', {
            from: t('appointment.slot', { name: p.opened.calendarName, day: day(a.start), time: time(a.start) }),
            to: t('appointment.slot', { name: p.target.calendarName, day: day(p.target.start), time: time(p.target.start) }),
          })}</p>
          <div className="flex flex-wrap gap-2">
            <Button variant="navy" disabled={p.working} onClick={p.onConfirmMove}>{t('appointment.move.confirm')}</Button>
            <Button variant="outline" disabled={p.working} onClick={p.onBack}>{t('appointment.back')}</Button>
          </div>
        </div>
      )}

      {p.mode === 'cancel' && <CancelForm {...p} />}
    </aside>
  );
}

function CancelForm(p: Props) {
  const { t } = useI18n();
  const { register, formState: { errors }, watch } = p.form;
  const reason = watch('reason');
  const reasonError = errors.reason ? 'appointment.cancel.error.reason' : null;
  const noteError = errors.note?.message === 'noteLong' ? 'appointment.cancel.error.noteLong' : errors.note ? 'appointment.cancel.error.note' : null;
  return (
    <form noValidate className="flex flex-col gap-3" onSubmit={e => { e.preventDefault(); p.onConfirmCancel(); }}>
      <h3 className="text-sm font-extrabold">{t('appointment.cancel.title')}</h3>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="cancel-reason">{t('appointment.cancel.reason')}</Label>
        <Select value={reason ?? ''} onValueChange={v => p.onReason(v as CancelForm['reason'])}>
          <SelectTrigger id="cancel-reason" aria-invalid={!!reasonError} aria-describedby={reasonError ? 'cancel-reason-error' : undefined} className="h-11 w-full">
            <SelectValue placeholder={t('appointment.cancel.choose')} />
          </SelectTrigger>
          <SelectContent>{cancelReasons.map(r => <SelectItem key={r} value={r}>{t(`cancelReason.${r}`)}</SelectItem>)}</SelectContent>
        </Select>
        {reasonError && <p id="cancel-reason-error" className="text-xs font-bold text-danger-700">{t(reasonError)}</p>}
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="cancel-note">{t(reason === 'Other' ? 'appointment.cancel.noteRequired' : 'appointment.cancel.note')}</Label>
        <Textarea id="cancel-note" rows={3} aria-invalid={!!noteError} aria-describedby={noteError ? 'cancel-note-error cancel-note-hint' : 'cancel-note-hint'} {...register('note')} />
        <p id="cancel-note-hint" className="text-xs text-muted-foreground">{t('appointment.cancel.noteHint')}</p>
        {noteError && <p id="cancel-note-error" className="text-xs font-bold text-danger-700">{t(noteError)}</p>}
      </div>
      <div className="flex flex-wrap gap-2">
        <Button type="submit" variant="navy" disabled={p.working}>{t('appointment.cancel.confirm')}</Button>
        <Button type="button" variant="outline" disabled={p.working} onClick={p.onBack}>{t('appointment.back')}</Button>
      </div>
    </form>
  );
}
