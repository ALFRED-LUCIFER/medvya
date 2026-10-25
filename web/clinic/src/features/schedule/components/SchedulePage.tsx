import { useRef, type ReactNode } from 'react';
import { ChevronLeft, ChevronRight, Plus } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import ShellMessage from '../../shell/components/ShellMessage';
import type { DayLayout, ScheduleColumn } from '../domain/schedule';
import { scheduleViews, type AgendaEntry, type ScheduleView, type WeekDay } from '../domain/views';
import AgendaView from './AgendaView';
import DayGrid, { type SlotHandler } from './DayGrid';
import { Legend } from './parts';
import WeekView from './WeekView';

export type { SlotHandler } from './DayGrid';

type Props = {
  view: ScheduleView;
  /** The facility-local date asked for, else today's answer. */
  date: string | null;
  /** Monday and Sunday of the week around `date`. */
  weekSpan: { from: string; to: string } | null;
  timeZone: string | null;
  calendars: { id: string; name: string }[];
  /** The doctor or resource shown; null shows every column (day and list only). */
  filter: string | null;
  day: DayLayout | null;
  agenda: { column: ScheduleColumn; entries: AgendaEntry[] }[];
  week: WeekDay[] | null;
  weekName: string;
  /** The previous data is still on screen while the asked-for one loads. */
  updating: boolean;
  loading: boolean;
  forbidden: boolean;
  failed: boolean;
  onView: (view: ScheduleView) => void;
  onFilter: (calendarId: string | null) => void;
  onOpenDay: (date: string) => void;
  onPrevious: () => void;
  onNext: () => void;
  onToday: () => void;
  onRetry: () => void;
  onSlot?: SlotHandler;
  /** Opens booking at the next free slot (the pinned "+ Book" on phones); absent when the role may not book or nothing is free. */
  onBookNext?: () => void;
  /** The booking panel (or its hint), beside the schedule on wide screens and above it on narrow ones. */
  panel?: ReactNode;
};

const all = '__all';

/** P-04 Schedule (MDV-P1-011, 021, 023): day, week and list views; times in the facility's zone. */
export default function SchedulePage(props: Props) {
  const { t, formatDay, formatShortDay } = useI18n();
  const panelRef = useRef<HTMLDivElement>(null);
  const { date, view, weekSpan } = props;
  const isWeek = view === 'week';
  const title = isWeek && weekSpan ? t('schedule.weekRange', { from: formatShortDay(weekSpan.from), to: formatShortDay(weekSpan.to) })
    : date ? formatDay(date) : t('nav.schedule');
  return (
    <div className="flex flex-col gap-4 max-md:pb-16">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="text-sm font-bold text-action-600">{t('nav.schedule')}</p>
          <h1 className="text-2xl font-extrabold tracking-tight">{title}</h1>
          {props.timeZone && <p className="text-sm text-muted-foreground">{t('schedule.facilityTime', { zone: props.timeZone })}</p>}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="outline" size="icon" aria-label={t(isWeek ? 'schedule.prevWeek' : 'schedule.prevDay')} disabled={!date} onClick={props.onPrevious}>
            <ChevronLeft className="rtl:rotate-180" aria-hidden />
          </Button>
          <Button variant="outline" onClick={props.onToday}>{t('schedule.today')}</Button>
          <Button variant="outline" size="icon" aria-label={t(isWeek ? 'schedule.nextWeek' : 'schedule.nextDay')} disabled={!date} onClick={props.onNext}>
            <ChevronRight className="rtl:rotate-180" aria-hidden />
          </Button>
          <div role="group" aria-label={t('schedule.views')} className="flex gap-1">
            {scheduleViews.map(v => (
              <Button key={v} variant={view === v ? 'navy' : 'outline'} aria-pressed={view === v} onClick={() => props.onView(v)}>{t(`schedule.view.${v}`)}</Button>
            ))}
          </div>
          {props.calendars.length > 1 && (
            <Select value={props.filter ?? all} onValueChange={v => props.onFilter(v === all ? null : v)}>
              <SelectTrigger aria-label={t('schedule.filter')} className="w-56 data-[size=default]:h-11"><SelectValue /></SelectTrigger>
              <SelectContent>
                {!isWeek && <SelectItem value={all}>{t('schedule.filter.all')}</SelectItem>}
                {props.calendars.map(c => <SelectItem key={c.id} value={c.id}>{c.name || t('schedule.doctor')}</SelectItem>)}
              </SelectContent>
            </Select>
          )}
        </div>
      </header>
      <Legend />
      <div className="grid items-start gap-5 lg:grid-cols-[minmax(0,1fr)_340px]">
        <Body {...props} />
        {props.panel && <div ref={panelRef} className="scroll-mt-20 max-lg:order-first lg:sticky lg:top-20">{props.panel}</div>}
      </div>
      {props.onBookNext && (
        // Pinned above the phone's bottom tabs; the panel it opens sits above the list, so it is brought into view.
        <Button className="fixed end-4 bottom-20 z-20 shadow-lg md:hidden"
          onClick={() => { props.onBookNext?.(); panelRef.current?.scrollIntoView({ block: 'start' }); }}>
          <Plus aria-hidden />{t('schedule.bookNext')}
        </Button>
      )}
    </div>
  );
}

// Offline is not handled here: the shell's OfflineBanner says it once for every screen.
function Body(p: Props) {
  const { t } = useI18n();
  if (p.forbidden) return <ShellMessage heading="h2" title={t('err.403')} body={t('schedule.forbidden.body')} />;
  if (p.failed)
    return (
      <div role="alert" className="flex flex-col items-start gap-3">
        <ShellMessage heading="h2" title={t('schedule.error.title')} body={t('schedule.error.body')} />
        <Button variant="navy" onClick={p.onRetry}>{t('schedule.retry')}</Button>
      </div>
    );
  if (p.loading || !p.day || !p.timeZone) return <ShellMessage heading="h2" busy title={t('schedule.loading')} />;
  if (p.day.columns.length === 0 && p.calendars.length === 0) return <ShellMessage heading="h2" title={t('schedule.empty.title')} body={t('schedule.empty.body')} />;
  if (p.view === 'week')
    return p.week ? <WeekView name={p.weekName} days={p.week} timeZone={p.timeZone} updating={p.updating} onOpenDay={p.onOpenDay} /> : <ShellMessage heading="h2" busy title={t('schedule.loading')} />;
  if (p.view === 'list') return <AgendaView sections={p.agenda} timeZone={p.timeZone} updating={p.updating} onSlot={p.onSlot} />;
  return <DayGrid day={p.day} updating={p.updating} onSlot={p.onSlot} />;
}
