import type { FormEventHandler, ReactNode } from 'react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { ArrowLeftRight, Clock, Stethoscope, Users } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, FrontDeskTag, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { minutesSince, type LiveStatus, type MoveError } from '../domain/board';
import { countsInOrder, reassignReasons, type ClinicOverview, type DoctorLoad, type ReassignInput } from '../domain/overview';

type Props = {
  overview: ClinicOverview | null;
  mine: DoctorLoad | null;
  loading: boolean;
  offline: boolean;
  online: boolean;
  live: LiveStatus;
  forbidden: boolean;
  failed: boolean;
  /** Full "Check-in & queue" access: may move waiting patients; otherwise read only. */
  canMove: boolean;
  form: UseFormReturn<ReassignInput>;
  movingId: string | null;
  moving: boolean;
  failure: { id: string; error: MoveError } | null;
  onRetry: () => void;
  onStartMove: (id: string) => void;
  onCancelMove: () => void;
  onConfirmMove: FormEventHandler<HTMLFormElement>;
  /** Link back to the board. */
  boardAction?: ReactNode;
};

/** D-04 Clinic overview: every doctor working today (visits per state, delay, next patients) and the shared waiting room. */
export default function ClinicOverviewPage(props: Props) {
  const { t } = useI18n();
  return (
    <div className="grid grid-cols-[minmax(0,1fr)] gap-4">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="text-[13px] font-bold text-action-600">{t('nav.frontDesk')}</p>
          <h1 className="text-[26px] font-extrabold tracking-tight">{t('overview.title')}</h1>
          {props.overview && <p className="text-[13px] text-muted-foreground">{t('schedule.facilityTime', { zone: props.overview.timeZone })}</p>}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {!props.forbidden && <p role="status" className="inline-flex min-h-9 items-center"><FrontDeskTag kind={props.live} /></p>}
          {props.boardAction}
        </div>
      </header>
      <Body {...props} />
    </div>
  );
}

function Message({ title, body, action, busy = false }: { title: string; body?: string; action?: { label: string; run: () => void }; busy?: boolean }) {
  return (
    <div role={busy ? 'status' : 'alert'} className="flex max-w-[560px] flex-col items-start gap-2 rounded-xl border bg-card p-6">
      <h2 className="text-lg font-extrabold">{title}</h2>
      {body && <p className="text-muted-foreground">{body}</p>}
      {action && <Button variant="navy" className="mt-2" onClick={action.run}>{action.label}</Button>}
    </div>
  );
}

function Body(props: Props) {
  const { t } = useI18n();
  const { overview } = props;
  if (props.forbidden) return <Message title={t('err.403')} body={t('board.forbidden.body')} />;
  if (props.offline) return <Message title={t('state.offline.title')} body={t('board.offline')} />;
  if (props.failed) return <Message title={t('overview.error.title')} body={t('board.error.body')} action={{ label: t('schedule.retry'), run: props.onRetry }} />;
  if (props.loading || !overview) return <Message busy title={t('overview.loading')} />;
  if (overview.doctors.length === 0) return <Message title={t('overview.empty.title')} body={t('overview.empty.body')} />;
  const now = Date.parse(overview.now);
  return (
    <>
      {props.mine && <Doctor doctor={props.mine} timeZone={overview.timeZone} mine />}
      <section aria-labelledby="overview-doctors" className="flex flex-col gap-2">
        <h2 id="overview-doctors" className="flex items-center gap-1.5 text-lg font-extrabold"><Stethoscope aria-hidden className="size-5" />{t('overview.doctors')}</h2>
        <ul className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {overview.doctors.map(d => <li key={d.calendarId}><Doctor doctor={d} timeZone={overview.timeZone} /></li>)}
        </ul>
      </section>
      <section aria-labelledby="overview-waiting" className="flex flex-col gap-2">
        <h2 id="overview-waiting" className="flex items-center gap-1.5 text-lg font-extrabold"><Users aria-hidden className="size-5" />{t('overview.waitingRoom', { count: overview.waiting.length })}</h2>
        {overview.waiting.length === 0 ? <p className="text-muted-foreground">{t('overview.waitingEmpty')}</p> : (
          <ul className="flex flex-col divide-y rounded-lg border bg-card">
            {overview.waiting.map(w => <Waiting key={w.id} visit={w} now={now} {...props} overview={overview} />)}
          </ul>
        )}
      </section>
    </>
  );
}

function Doctor({ doctor, timeZone, mine = false }: { doctor: DoctorLoad; timeZone: string; mine?: boolean }) {
  const { t, formatTime } = useI18n();
  const id = `overview-doctor-${mine ? 'mine' : doctor.calendarId}`;
  return (
    <article aria-labelledby={id} className={`flex h-full flex-col gap-2 rounded-xl border bg-card p-4 ${mine ? 'border-action-600' : ''}`}>
      <h3 id={id} className="font-extrabold">{mine ? t('overview.mine', { name: doctor.name }) : doctor.name}</h3>
      <p className="flex items-center gap-1.5 text-sm font-bold">
        <Clock aria-hidden className="size-4" />{doctor.delayMinutes > 0 ? t('overview.late', { minutes: doctor.delayMinutes }) : t('overview.onTime')}
      </p>
      <ul aria-label={t('overview.byState')} className="flex flex-wrap gap-x-3 gap-y-1 text-sm text-muted-foreground">
        {countsInOrder(doctor.counts).map(c => <li key={c.state}>{t('overview.count', { state: t(`board.column.${c.state}`), count: c.count })}</li>)}
      </ul>
      <h4 className="text-sm font-bold">{t('overview.next')}</h4>
      {doctor.next.length === 0 ? <p className="text-sm text-muted-foreground">{t('overview.nextNone')}</p> : (
        <ol className="flex flex-col gap-1 text-sm">
          {doctor.next.map(n => (
            <li key={n.id} className="flex flex-wrap gap-x-2">
              <span className="font-mono">{formatTime(new Date(n.start), timeZone)}</span>
              <span className="font-semibold">{n.patientName || t('board.unknownPatient')}</span>
              {n.token && <span>{t('board.token', { token: n.token })}</span>}
              <span className="text-muted-foreground">{t(`board.column.${n.state as 'Booked'}`)}</span>
            </li>
          ))}
        </ol>
      )}
    </article>
  );
}

function Waiting({ visit, now, overview, canMove, online, form, movingId, moving, failure, onStartMove, onCancelMove, onConfirmMove }: Props & { visit: ClinicOverview['waiting'][number]; now: number; overview: ClinicOverview }) {
  const { t } = useI18n();
  const doctor = overview.doctors.find(d => d.calendarId === visit.calendarId)?.name ?? '';
  const others = overview.doctors.filter(d => d.calendarId !== visit.calendarId);
  const nameId = `waiting-${visit.id}`;
  const error = failure?.id === visit.id ? failure.error : null;
  const { errors } = form.formState;
  return (
    <li className="flex flex-col gap-2 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="flex flex-col">
          <span id={nameId} className="font-bold">{visit.patientName || t('board.unknownPatient')}</span>
          <span className="text-sm text-muted-foreground">
            {[visit.token && t('board.token', { token: visit.token }), t('overview.with', { doctor }), t(`board.column.${visit.state as 'CheckedIn'}`)].filter(Boolean).join(' · ')}
          </span>
        </span>
        <span className="flex flex-wrap items-center gap-2">
          <FrontDeskTag kind="waiting" text={t('board.waiting', { minutes: minutesSince(visit.since, now) })} />
          {canMove && others.length > 0 && movingId !== visit.id && (
            <Button variant="outline" className="min-h-11" disabled={!online} aria-describedby={nameId} onClick={() => onStartMove(visit.id)}>
              <ArrowLeftRight aria-hidden />{t('overview.move')}
            </Button>
          )}
        </span>
      </div>
      {canMove && movingId === visit.id && (
        <form noValidate onSubmit={onConfirmMove} aria-label={t('overview.moveTitle', { name: visit.patientName })} className="flex flex-wrap items-end gap-3 rounded-md border border-action-600 p-3">
          <div className="flex min-w-52 flex-col gap-1.5">
            <Label htmlFor={`move-to-${visit.id}`}>{t('overview.moveTo')}</Label>
            <Controller control={form.control} name="calendarId" render={({ field }) => (
              <Select value={field.value} onValueChange={field.onChange}>
                <SelectTrigger id={`move-to-${visit.id}`} className="h-11 w-full" aria-invalid={!!errors.calendarId}>
                  <SelectValue placeholder={t('overview.chooseDoctor')} />
                </SelectTrigger>
                <SelectContent>{others.map(d => <SelectItem key={d.calendarId} value={d.calendarId}>{d.name}</SelectItem>)}</SelectContent>
              </Select>
            )} />
          </div>
          <div className="flex min-w-52 flex-col gap-1.5">
            <Label htmlFor={`move-why-${visit.id}`}>{t('overview.reason')}</Label>
            <Controller control={form.control} name="reason" render={({ field }) => (
              <Select value={field.value} onValueChange={field.onChange}>
                <SelectTrigger id={`move-why-${visit.id}`} className="h-11 w-full"><SelectValue /></SelectTrigger>
                <SelectContent>{reassignReasons.map(r => <SelectItem key={r} value={r}>{t(`overview.reason.${r}`)}</SelectItem>)}</SelectContent>
              </Select>
            )} />
          </div>
          <Button type="submit" className="min-h-11" loading={moving} disabled={!online}>{t('overview.confirmMove')}</Button>
          <Button type="button" variant="outline" className="min-h-11" onClick={onCancelMove}>{t('booking.cancel')}</Button>
          {errors.calendarId && <p role="alert" className="basis-full text-sm font-semibold text-danger-700">{t('overview.chooseDoctor')}</p>}
        </form>
      )}
      {error && <p role="alert" className="text-sm font-bold text-danger-700">{t(`board.error.${error}`)}</p>}
    </li>
  );
}
