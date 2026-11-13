import type { UseFormReturn } from 'react-hook-form';
import { Link } from 'react-router';
import { CircleAlert, CircleCheck, X } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, buttonVariants, cn } from '@medvya/ui';
import { codeKey, needsAttention, type PatientMessage } from '../domain/messages';
import { actions, localDate, type Action, type CancelForm, type ChangeError, type Rule, type VisitMove } from '../domain/changes';
import type { Appointment } from '../domain/schedule';
import { termsPath } from '../../settings/domain/settings';
import CancelReasonFields from './CancelReasonFields';
import { StateLabel } from './StateLabel';

type Props = {
  timeZone: string;
  opened: { appointment: Appointment; calendarName: string };
  mode: 'view' | 'pick' | 'confirmMove' | 'cancel';
  target: { calendarName: string; start: string } | null;
  done: { kind: 'moved' | 'cancelled' | 'cancelledFollowing' | 'noShow' | 'checkedIn'; appointment: Appointment; calendarName: string; count: number } | null;
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
  /** MDV-P1-026: cancel this visit and the later ones of its series, one reason. */
  onConfirmCancelFollowing: () => void;
  onNoShow: () => void;
  onCheckIn: () => void;
  onBack: () => void;
  /** After a no-show: book the same patient again (opens booking with the patient filled in). */
  onRebook?: () => void;
  /** What the patient was told (MDV-P1-032/033). */
  messages: PatientMessage[] | 'loading' | 'error';
  /** Where the visit was before: every reschedule and queue move (MDV-P1-024). */
  history: VisitMove[] | 'loading' | 'error';
  /** The booking is saved but the job has not queued its messages yet (#912). */
  messagesPreparing: boolean;
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
          <p className="flex items-center gap-2 text-sm font-bold text-success-700"><CircleCheck aria-hidden className="size-5" />{t(`appointment.done.${p.done.kind}`, { time: time(a.start), count: p.done.count })}</p>
          {p.done.kind === 'noShow' && p.onRebook && <Button variant="default" onClick={p.onRebook}>{t('appointment.rebook', { name: a.patientName })}</Button>}
        </div>
      )}

      {p.error && (
        <div role="alert" className="flex flex-col items-start gap-1 text-sm font-bold text-danger-700">
          <p className="flex items-start gap-2"><CircleAlert aria-hidden className="mt-0.5 size-4 shrink-0" />{t(`appointment.error.${p.error}`)}</p>
          {p.error === 'terms_not_accepted' && <Link to={termsPath} className={cn(buttonVariants({ variant: 'link' }), 'px-0')}>{t('terms.open')}</Link>}
        </div>
      )}

      {!p.done && p.mode === 'view' && (
        <ul className="flex flex-col gap-2">
          {actions.map(k => {
            const rule = p.rules[k];
            const why = `appointment-why-${k}`;
            return (
              <li key={k} className="flex flex-col gap-1">
                <Button variant={k === 'cancel' || k === 'noShow' ? 'outline' : 'default'} disabled={!rule.allowed || p.working} onClick={run[k]}
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
            <Button variant="default" disabled={p.working} onClick={p.onConfirmMove}>{t('appointment.move.confirm')}</Button>
            <Button variant="outline" disabled={p.working} onClick={p.onBack}>{t('appointment.back')}</Button>
          </div>
        </div>
      )}

      {p.mode === 'cancel' && <CancelForm {...p} />}

      <History history={p.history} timeZone={p.timeZone} />
      <Messages messages={p.messages} preparing={p.messagesPreparing} timeZone={p.timeZone} />
    </aside>
  );
}

function CancelForm(p: Props) {
  const { t } = useI18n();
  return (
    <form noValidate className="flex flex-col gap-3" onSubmit={e => { e.preventDefault(); p.onConfirmCancel(); }}>
      <h3 className="text-sm font-extrabold">{t('appointment.cancel.title')}</h3>
      <CancelReasonFields form={p.form} onReason={p.onReason} />
      <div className="flex flex-wrap gap-2">
        <Button type="submit" variant="default" disabled={p.working}>{t('appointment.cancel.confirm')}</Button>
        {p.opened.appointment.seriesId && (
          <Button type="button" variant="default" disabled={p.working} onClick={p.onConfirmCancelFollowing}>{t('appointment.cancel.following')}</Button>
        )}
        <Button type="button" variant="outline" disabled={p.working} onClick={p.onBack}>{t('appointment.back')}</Button>
      </div>
    </form>
  );
}

/** The visit's moves, oldest first: from which slot to which, who moved it and when. Shown only when it has moved. */
function History({ history, timeZone }: { history: Props['history']; timeZone: string }) {
  const { t, formatShortDay, formatTime } = useI18n();
  if (Array.isArray(history) && history.length === 0) return null;
  const slot = (name: string, iso: string) =>
    t('appointment.slot', { name: name || t('appointment.history.unknown'), day: formatShortDay(localDate(iso, timeZone)), time: formatTime(new Date(iso), timeZone) });
  return (
    <section aria-labelledby="appointment-history" className="flex flex-col gap-2 border-t pt-3">
      <h3 id="appointment-history" className="text-sm font-extrabold">{t('appointment.history.title')}</h3>
      {history === 'loading' && <p role="status" className="text-xs text-muted-foreground">{t('appointment.history.loading')}</p>}
      {history === 'error' && <p role="alert" className="text-xs font-bold text-danger-700">{t('appointment.history.error')}</p>}
      {Array.isArray(history) && (
        <ol className="flex flex-col gap-1.5">
          {history.map(m => (
            <li key={m.occurredAt + m.toStart} className="flex flex-col text-sm">
              <span>{t('appointment.history.move', { from: slot(m.fromCalendarName, m.fromStart), to: slot(m.toCalendarName, m.toStart) })}</span>
              <span className="text-xs text-muted-foreground">
                {t(m.reassignReason ? 'appointment.history.byWhy' : 'appointment.history.by', { name: m.actorName || t('appointment.history.unknown'),
                  day: formatShortDay(localDate(m.occurredAt, timeZone)), time: formatTime(new Date(m.occurredAt), timeZone), why: m.reassignReason ? t(`overview.reason.${m.reassignReason}`) : '' })}
              </span>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}

/** The messages sent to the patient about this visit. A failed one says so in words and with an icon, never by colour alone. */
function Messages({ messages, preparing, timeZone }: { messages: Props['messages']; preparing: boolean; timeZone: string }) {
  const { t, formatShortDay, formatTime } = useI18n();
  return (
    <section aria-labelledby="appointment-messages" className="flex flex-col gap-2 border-t pt-3">
      <h3 id="appointment-messages" className="text-sm font-extrabold">{t('appointment.messages.title')}</h3>
      {messages === 'loading' && <p role="status" className="text-xs text-muted-foreground">{t('appointment.messages.loading')}</p>}
      {messages === 'error' && <p role="alert" className="text-xs font-bold text-danger-700">{t('appointment.messages.error')}</p>}
      {preparing && <p role="status" className="text-xs text-muted-foreground">{t('appointment.messages.preparing')}</p>}
      {Array.isArray(messages) && messages.length === 0 && !preparing && <p className="text-xs text-muted-foreground">{t('appointment.messages.empty')}</p>}
      {Array.isArray(messages) && messages.length > 0 && (
        <ul className="flex flex-col gap-1.5">
          {messages.map(m => {
            const code = codeKey(m.failureCode);
            const when = new Date(m.sendAt);
            return (
              <li key={m.id} className="flex flex-col text-sm">
                <span className="flex items-center gap-2">
                  {needsAttention(m) && <CircleAlert aria-hidden className="size-4 shrink-0 text-danger-700" />}
                  <span className={needsAttention(m) ? 'font-bold text-danger-700' : 'font-bold'}>
                    {t('appointment.messages.line', { kind: t(`message.kind.${m.kind}`), channel: t(`message.channel.${m.channel}`), status: t(`message.status.${m.status}`) })}
                  </span>
                </span>
                <span className="text-xs text-muted-foreground">
                  {t(code ? 'appointment.messages.atWhy' : 'appointment.messages.at',
                    { day: formatShortDay(localDate(m.sendAt, timeZone)), time: formatTime(when, timeZone), why: code ? t(`message.code.${code}`) : '' })}
                </span>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
