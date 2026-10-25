import { useI18n } from '@medvya/i18n';
import { Button, cn, scheduleLooks } from '@medvya/ui';
import type { ScheduleColumn } from '../domain/schedule';
import type { AgendaEntry } from '../domain/views';
import type { GridActions } from './DayGrid';
import { hatched } from './looks';
import { AppointmentChip, OpenableChip } from './parts';

type Props = { sections: { column: ScheduleColumn; entries: AgendaEntry[] }[]; timeZone: string; updating: boolean } & Pick<GridActions, 'onSlot' | 'onOpen' | 'moving'>;

/** List view (agenda): the day per doctor and resource in time order. The default on phones; also prints cleanly. */
export default function AgendaView({ sections, timeZone, updating, ...actions }: Props) {
  const { t, formatTime } = useI18n();
  const time = (iso: string) => formatTime(new Date(iso), timeZone);
  return (
    <div aria-busy={updating || undefined} className={cn('flex flex-col gap-4 transition-opacity', updating && 'opacity-60')}>
      {sections.map(({ column, entries }) => {
        const name = column.name || t('schedule.doctor');
        return (
          <section key={column.calendarId} aria-labelledby={`agenda-${column.calendarId}`} className="rounded-lg border bg-card">
            <h2 id={`agenda-${column.calendarId}`} className="border-b px-4 py-2.5 text-sm font-bold">{name}</h2>
            <ul className="flex flex-col divide-y">
              {entries.length === 0 && <li className="px-4 py-3 text-sm text-muted-foreground">{t('schedule.column.noSchedule')}</li>}
              {entries.map(e => (
                <li key={`${e.kind}-${e.start}`} className="flex items-center gap-3 px-4 py-1.5">
                  <span className="w-12 shrink-0 font-mono text-xs text-muted-foreground">{time(e.start)}</span>
                  <Entry entry={e} name={name} calendarId={column.calendarId} time={time} {...actions} />
                </li>
              ))}
            </ul>
          </section>
        );
      })}
    </div>
  );
}

function Entry({ entry: e, name, calendarId, time, onSlot, onOpen, moving }: { entry: AgendaEntry; name: string; calendarId: string; time: (iso: string) => string } & Pick<GridActions, 'onSlot' | 'onOpen' | 'moving'>) {
  const { t } = useI18n();
  if (e.kind === 'appointment')
    return onOpen
      ? <OpenableChip appointment={e} time={time} wrap className="min-w-0 flex-1" onOpen={() => onOpen(e, calendarId, name)} draggable={false} />
      : <AppointmentChip appointment={e} time={time} wrap className="min-w-0 flex-1" />;
  if (e.kind === 'unavailable') {
    const reason = e.label ? t('schedule.reasonWithLabel', { reason: t(`schedule.reason.${e.reason}`), label: e.label }) : t(`schedule.reason.${e.reason}`);
    const Icon = scheduleLooks.unavailable.icon;
    return (
      <span className={cn('flex min-h-9 flex-1 items-center gap-1.5 rounded-md border px-2 text-xs font-bold', hatched)} style={{ color: scheduleLooks.unavailable.fg }}>
        <Icon aria-hidden className="size-3.5" />{t('schedule.unavailable', { from: time(e.start), to: time(e.end), reason })}
      </span>
    );
  }
  if (e.held) {
    const look = scheduleLooks.held, Icon = look.icon;
    return <span className="flex min-h-9 flex-1 items-center gap-1.5 rounded-md px-2 text-xs font-bold" style={{ backgroundColor: look.bg, color: look.fg }}><Icon aria-hidden className="size-3.5" />{t('schedule.slot.held', { time: time(e.start) })}</span>;
  }
  return onSlot
    // The visible word is part of the full name (WCAG 2.5.3); the row's time column shows the time.
    ? <Button variant="outline" className="flex-1 justify-start" onClick={() => onSlot(calendarId, name, e.start)}
        aria-label={moving ? t('schedule.slot.moveTo', { patient: moving, name, time: time(e.start) }) : t('schedule.slot.book', { name, time: time(e.start) })}>
        {t(moving ? 'schedule.slot.moveShort' : 'schedule.slot.bookShort')}
      </Button>
    : <span className="flex-1 text-sm text-muted-foreground">{t('schedule.slot.free', { time: time(e.start) })}</span>;
}
