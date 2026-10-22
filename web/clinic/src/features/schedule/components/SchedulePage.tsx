import type { ReactNode } from 'react';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, cn, scheduleLooks } from '@medvya/ui';
import ShellMessage from '../../shell/components/ShellMessage';
import type { ColumnItem, ColumnLayout, DayLayout } from '../domain/schedule';

/** Called when an empty slot is clicked; absent when the role may not book (the grid is then read-only). */
export type SlotHandler = (calendarId: string, calendarName: string, start: string) => void;

type Props = {
  day: DayLayout | null;
  date: string | null;
  /** The previous day is still on screen while the asked-for one loads. */
  updating: boolean;
  loading: boolean;
  forbidden: boolean;
  failed: boolean;
  onPrevious: () => void;
  onNext: () => void;
  onToday: () => void;
  onRetry: () => void;
  onSlot?: SlotHandler;
  /** The booking panel (or its hint), shown beside the grid on wide screens and above it on narrow ones. */
  panel?: ReactNode;
};

/** P-04 Schedule, day view: a column per doctor and resource, times in the facility's zone. */
export default function SchedulePage(props: Props) {
  const { t, formatDay } = useI18n();
  const { date, day } = props;
  return (
    <div className="flex flex-col gap-4">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="text-sm font-bold text-action-600">{t('nav.schedule')}</p>
          <h1 className="text-2xl font-extrabold tracking-tight">{date ? formatDay(date) : t('nav.schedule')}</h1>
          {day && <p className="text-sm text-muted-foreground">{t('schedule.facilityTime', { zone: day.timeZone })}</p>}
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
      <div className="grid items-start gap-5 lg:grid-cols-[minmax(0,1fr)_340px]">
        <Body {...props} />
        {props.panel && <div className="max-lg:order-first lg:sticky lg:top-20">{props.panel}</div>}
      </div>
    </div>
  );
}

// Offline is not handled here: the shell's OfflineBanner says it once for every screen.
function Body({ day, updating, loading, forbidden, failed, onRetry, onSlot }: Props) {
  const { t } = useI18n();
  if (forbidden) return <ShellMessage heading="h2" title={t('err.403')} body={t('schedule.forbidden.body')} />;
  if (failed)
    return (
      <div role="alert" className="flex flex-col items-start gap-3">
        <ShellMessage heading="h2" title={t('schedule.error.title')} body={t('schedule.error.body')} />
        <Button variant="navy" onClick={onRetry}>{t('schedule.retry')}</Button>
      </div>
    );
  if (loading || !day) return <ShellMessage heading="h2" busy title={t('schedule.loading')} />;
  if (day.columns.length === 0) return <ShellMessage heading="h2" title={t('schedule.empty.title')} body={t('schedule.empty.body')} />;
  return <DayGrid day={day} updating={updating} onSlot={onSlot} />;
}

function DayGrid({ day, updating, onSlot }: { day: DayLayout; updating: boolean; onSlot?: SlotHandler }) {
  const { t, formatTime } = useI18n();
  const time = (iso: string) => formatTime(new Date(iso), day.timeZone);
  // The grid scrolls inside its own box so headers stick to its top and the time axis to its reading-start side.
  // Focusable, so keyboard users can scroll it too (WCAG 2.1.1); the global focus ring shows where focus is.
  return (
    <section aria-label={t('schedule.grid')} aria-busy={updating || undefined} tabIndex={0}
      className={cn('max-h-[calc(100dvh-12rem)] min-h-80 overflow-auto rounded-lg border bg-card transition-opacity', updating && 'opacity-60')}>
      <div className="grid" style={{ gridTemplateColumns: `56px repeat(${day.columns.length}, minmax(168px, 1fr))` }}>
        <div className="sticky start-0 top-0 z-30 border-e border-b bg-card" />
        {day.columns.map(c => <ColumnHeader key={c.calendarId} column={c} />)}
        <div aria-hidden className="sticky start-0 z-10 border-e bg-card" style={{ height: day.height }}>
          {day.marks.map(m => (
            <span key={m.at} className="absolute inset-x-0 border-t px-2 pt-0.5 font-mono text-xs text-muted-foreground" style={{ top: m.top }}>{time(m.at)}</span>
          ))}
        </div>
        {day.columns.map(c => (
          <ul key={c.calendarId} aria-labelledby={`schedule-col-${c.calendarId}`} className="relative border-s" style={{ height: day.height }}>
            {c.items.map(i => <Item key={`${i.kind}-${i.start}`} item={i} column={c} time={time} onSlot={onSlot} />)}
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
    <div className="sticky top-0 z-20 border-s border-b bg-card px-3 py-2.5">
      <h2 id={`schedule-col-${c.calendarId}`} className="text-sm font-bold">{c.name || t('schedule.doctor')}</h2>
      <p className="text-xs text-muted-foreground">{sub}</p>
    </div>
  );
}

const hatched = 'bg-[repeating-linear-gradient(45deg,var(--color-muted)_0_6px,var(--color-line)_6px_12px)]';
/** Below this height the words do not fit; the block then shows its icon and names the reason as its tooltip. */
const minLabelPx = 32;

function Item({ item, column, time, onSlot }: { item: ColumnItem; column: ColumnLayout; time: (iso: string) => string; onSlot?: SlotHandler }) {
  const { t } = useI18n();
  const box = { top: item.top, height: item.height };
  const name = column.name || t('schedule.doctor');

  if (item.kind === 'slot' && item.held) {
    const look = scheduleLooks.held, Icon = look.icon;
    return (
      <li className="absolute inset-x-1 my-0.5 flex items-start gap-1 overflow-hidden rounded-md px-2 py-1 text-xs font-bold" style={{ ...box, backgroundColor: look.bg, color: look.fg }}>
        <Icon aria-hidden className="mt-px size-3.5 shrink-0" />{t('schedule.slot.held', { time: time(item.start) })}
      </li>
    );
  }
  if (item.kind === 'slot')
    return (
      <li className="absolute inset-x-0 border-t" style={box}>
        {onSlot ? (
          <button type="button" aria-label={t('schedule.slot.book', { name, time: time(item.start) })} onClick={() => onSlot(column.calendarId, name, item.start)}
            className="size-full px-2 pt-0.5 text-start align-top hover:bg-accent focus-visible:-outline-offset-2">
            <span aria-hidden className="font-mono text-xs text-muted-foreground">{time(item.start)}</span>
          </button>
        ) : (
          <span className="block px-2 pt-0.5">
            <span aria-hidden className="font-mono text-xs text-muted-foreground">{time(item.start)}</span>
            <span className="sr-only">{t('schedule.slot.free', { time: time(item.start) })}</span>
          </span>
        )}
      </li>
    );
  if (item.kind === 'appointment') {
    const look = scheduleLooks.booked, Icon = look.icon;
    return (
      <li className="absolute inset-x-1 z-[1] my-0.5 flex flex-col gap-0.5 overflow-hidden rounded-lg px-2 py-1" style={{ ...box, backgroundColor: look.bg, color: look.fg, border: look.border }}>
        <span className="truncate text-sm font-bold text-foreground">{item.patientName}</span>
        <span className="flex items-center gap-1 truncate text-xs">
          <Icon aria-hidden className="size-3.5 shrink-0" />
          <strong className="font-extrabold">{t(`schedule.state.${item.state}`)}</strong>
          {t('schedule.appointment.detail', { time: time(item.start), type: t(`visitType.${item.visitType}`) })}
        </span>
      </li>
    );
  }
  const look = scheduleLooks.unavailable, Icon = look.icon;
  const reason = item.label ? t('schedule.reasonWithLabel', { reason: t(`schedule.reason.${item.reason}`), label: item.label }) : t(`schedule.reason.${item.reason}`);
  const words = t('schedule.unavailableBlock', { reason });
  const compact = item.height < minLabelPx;
  return (
    <li title={compact ? words : undefined} className={cn('absolute inset-x-1 my-0.5 overflow-hidden rounded-md border', hatched, compact ? 'grid place-items-center' : 'px-2 py-1')}
      style={{ ...box, color: look.fg }}>
      <span aria-hidden className="flex items-center gap-1 text-xs font-bold">
        <Icon className="size-3.5 shrink-0" />{!compact && words}
      </span>
      <span className="sr-only">{t('schedule.unavailable', { from: time(item.start), to: time(item.end), reason })}</span>
    </li>
  );
}
