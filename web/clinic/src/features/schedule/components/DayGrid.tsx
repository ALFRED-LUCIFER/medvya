import type { DragEvent } from 'react';
import { useI18n } from '@medvya/i18n';
import { cn, scheduleLooks } from '@medvya/ui';
import type { CalendarKind, ColumnItem, ColumnLayout, DayLayout } from '../domain/schedule';
import { hatched } from './looks';
import { AppointmentChip, dragType, OpenableChip, type DropHandler, type OpenHandler } from './parts';

/** Called when an empty slot is clicked; absent when the role may not book (the grid is then read-only). */
export type SlotHandler = (calendarId: string, calendarName: string, start: string) => void;

/** What the grid can do beyond showing: book or pick an empty slot, open a visit, drop a dragged visit on a slot. */
export type GridActions = {
  onSlot?: SlotHandler;
  onOpen?: OpenHandler;
  onDrop?: DropHandler;
  /**
   * The visit being rescheduled: empty slots of calendars of its kind then read "Move … to …" instead of "Book …"; other
   * calendars offer nothing, as the server would refuse (a doctor's visit never moves into a room's calendar).
   */
  moving?: { patient: string; kind: CalendarKind };
};

/** Day view: a column per doctor and resource on one time scale, so a 40-minute visit spans two 20-minute slots. */
export default function DayGrid({ day, updating, ...actions }: { day: DayLayout; updating: boolean } & GridActions) {
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
            {c.items.map(i => <Item key={`${i.kind}-${i.start}`} item={i} column={c} day={day} time={time} {...actions} />)}
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

/** Below this height the words do not fit; the block then shows its icon and names the reason as its tooltip. */
const minLabelPx = 32;

function Item({ item, column, day, time, onSlot, onOpen, onDrop, moving }: { item: ColumnItem; column: ColumnLayout; day: DayLayout; time: (iso: string) => string } & GridActions) {
  const { t } = useI18n();
  const box = { top: item.top, height: item.height };
  const name = column.name || t('schedule.doctor');
  // A visit dragged from any column of this day lands here; the panel confirms before anything moves.
  const drop = (start: string) => (e: DragEvent) => {
    e.preventDefault();
    const id = e.dataTransfer.getData(dragType);
    const from = day.columns.find(c => c.items.some(i => i.kind === 'appointment' && i.id === id));
    const visit = from?.items.find(i => i.kind === 'appointment' && i.id === id);
    if (from && visit?.kind === 'appointment' && from.calendarKind === column.calendarKind) onDrop?.(visit, { calendarId: from.calendarId, calendarName: from.name || t('schedule.doctor') }, { calendarId: column.calendarId, calendarName: name, start });
  };

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
        {onSlot && (!moving || moving.kind === column.calendarKind) ? (
          <button type="button" aria-label={moving ? t('schedule.slot.moveTo', { patient: moving.patient, name, time: time(item.start) }) : t('schedule.slot.book', { name, time: time(item.start) })}
            onClick={() => onSlot(column.calendarId, name, item.start)}
            onDragOver={onDrop ? e => e.preventDefault() : undefined} onDrop={onDrop ? drop(item.start) : undefined}
            className={cn('size-full px-2 pt-0.5 text-start align-top hover:bg-accent focus-visible:-outline-offset-2', moving && 'bg-accent/40')}>
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
  if (item.kind === 'appointment')
    return (
      <li className="absolute inset-x-1 z-[1] my-0.5" style={box}>
        {onOpen
          ? <OpenableChip appointment={item} time={time} className="h-full" onOpen={() => onOpen(item, column.calendarId, name)} draggable={!!onDrop && item.state === 'Booked'} />
          : <AppointmentChip appointment={item} time={time} className="h-full" />}
      </li>
    );
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
