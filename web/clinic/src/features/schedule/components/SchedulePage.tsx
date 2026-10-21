import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, scheduleLooks } from '@medvya/ui';
import type { ColumnItem, ColumnLayout, DayLayout } from '../domain/schedule';

type Props = {
  day: DayLayout | null;
  date: string | null;
  loading: boolean;
  offline: boolean;
  forbidden: boolean;
  failed: boolean;
  onPrevious: () => void;
  onNext: () => void;
  onToday: () => void;
  onRetry: () => void;
};

/** P-04 Schedule, day view: a column per doctor and resource, times in the facility's zone. */
export default function SchedulePage(props: Props) {
  const { t, formatDay } = useI18n();
  const { date, day } = props;
  return (
    <div className="flex flex-col gap-4">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="text-[13px] font-bold text-action-600">{t('nav.schedule')}</p>
          <h1 className="text-[26px] font-extrabold tracking-tight">{date ? formatDay(date) : t('nav.schedule')}</h1>
          {day && <p className="text-[13px] text-muted-foreground">{t('schedule.facilityTime', { zone: day.timeZone })}</p>}
        </div>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="icon" aria-label={t('schedule.prevDay')} disabled={!date} onClick={props.onPrevious}>
            <ChevronLeft className="rtl:rotate-180" aria-hidden />
          </Button>
          <Button variant="outline" onClick={props.onToday}>{t('schedule.today')}</Button>
          <Button variant="outline" size="icon" aria-label={t('schedule.nextDay')} disabled={!date} onClick={props.onNext}>
            <ChevronRight className="rtl:rotate-180" aria-hidden />
          </Button>
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

function Body({ day, loading, offline, forbidden, failed, onRetry }: Props) {
  const { t } = useI18n();
  if (forbidden) return <Message title={t('err.403')} body={t('schedule.forbidden.body')} />;
  if (offline) return <Message title={t('state.offline.title')} body={t('schedule.offline')} />;
  if (failed) return <Message title={t('schedule.error.title')} body={t('schedule.error.body')} action={{ label: t('schedule.retry'), run: onRetry }} />;
  if (loading || !day) return <Message busy title={t('schedule.loading')} />;
  if (day.columns.length === 0) return <Message title={t('schedule.empty.title')} body={t('schedule.empty.body')} />;
  return <DayGrid day={day} />;
}

function DayGrid({ day }: { day: DayLayout }) {
  const { t, formatTime } = useI18n();
  const time = (iso: string) => formatTime(new Date(iso), day.timeZone);
  return (
    <section aria-label={t('schedule.grid')} className="overflow-auto rounded-lg border bg-card">
      <div className="grid" style={{ gridTemplateColumns: `56px repeat(${day.columns.length}, minmax(168px, 1fr))` }}>
        <div className="sticky top-0 z-10 border-b bg-card" />
        {day.columns.map(c => <ColumnHeader key={c.calendarId} column={c} />)}
        <div aria-hidden className="relative" style={{ height: day.height }}>
          {day.marks.map(m => (
            <span key={m.at} className="absolute inset-x-0 border-t px-2 pt-0.5 font-mono text-[11.5px] text-muted-foreground" style={{ top: m.top }}>{time(m.at)}</span>
          ))}
        </div>
        {day.columns.map(c => (
          <ul key={c.calendarId} aria-labelledby={`schedule-col-${c.calendarId}`} className="relative border-s" style={{ height: day.height }}>
            {c.items.map(i => <Item key={`${i.kind}-${i.start}`} item={i} time={time} />)}
          </ul>
        ))}
      </div>
    </section>
  );
}

function ColumnHeader({ column: c }: { column: ColumnLayout }) {
  const { t } = useI18n();
  const sub = !c.working ? t(c.slotMinutes === null ? 'schedule.column.noSchedule' : 'schedule.column.notIn')
    : c.rooms.length > 0 ? t('schedule.column.slots', { room: c.rooms.join(', '), minutes: c.slotMinutes ?? 0 })
    : t('schedule.column.slotsOnly', { minutes: c.slotMinutes ?? 0 });
  return (
    <div className="sticky top-0 z-10 border-s border-b bg-card px-3 py-2.5">
      <h2 id={`schedule-col-${c.calendarId}`} className="text-sm font-bold">{c.name || t('schedule.doctor')}</h2>
      <p className="text-xs text-muted-foreground">{sub}</p>
    </div>
  );
}

const hatched = 'bg-[repeating-linear-gradient(45deg,var(--color-muted)_0_6px,var(--color-line)_6px_12px)]';

function Item({ item, time }: { item: ColumnItem; time: (iso: string) => string }) {
  const { t } = useI18n();
  const box = { top: item.top, height: item.height };
  if (item.kind === 'slot')
    return (
      <li className="absolute inset-x-0 border-t px-2 pt-0.5" style={box}>
        <span aria-hidden className="font-mono text-[11.5px] text-muted-foreground">{time(item.start)}</span>
        <span className="sr-only">{t('schedule.slot.free', { time: time(item.start) })}</span>
      </li>
    );
  const look = scheduleLooks.unavailable, Icon = look.icon;
  const reason = item.label ? t('schedule.reasonWithLabel', { reason: t(`schedule.reason.${item.reason}`), label: item.label }) : t(`schedule.reason.${item.reason}`);
  return (
    <li className={`absolute inset-x-1 my-0.5 overflow-hidden rounded-md border px-2 py-1 ${hatched}`} style={{ ...box, color: look.fg }}>
      <span aria-hidden className="flex items-center gap-1 text-xs font-bold">
        <Icon className="size-3.5 shrink-0" />{t('schedule.unavailableBlock', { reason })}
      </span>
      <span className="sr-only">{t('schedule.unavailable', { from: time(item.start), to: time(item.end), reason })}</span>
    </li>
  );
}
