import { useRef, type ReactNode } from 'react';
import { flushSync } from 'react-dom';
import { Link } from 'react-router';
import { CalendarOff, ChevronLeft, ChevronRight, Info, Ellipsis, Plus, Printer } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, cn, DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger, EmptyState, FocusScope, PageContainer, PageHeader, PageMessage, SegmentedControl, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Skeleton, SkeletonGroup } from '@medvya/ui';
import type { DayLayout, ScheduleColumn } from '../domain/schedule';
import { scheduleViews, type AgendaEntry, type ScheduleView, type WeekDay } from '../domain/views';
import AgendaView from './AgendaView';
import type { DaySheet } from '../domain/daySheet';
import DayGrid, { type Earlier, type GridActions } from './DayGrid';
import DaySheetView from './DaySheetView';
import { Legend } from './Legend';
import WeekView from './WeekView';

export type { GridActions, SlotHandler } from './DayGrid';

type Props = {
  view: ScheduleView;
  /** The facility-local date asked for, else today's answer. */
  date: string | null;
  /** Monday and Sunday of the week around `date`. */
  weekSpan: { from: string; to: string } | null;
  timeZone: string | null;
  /** This device is in another zone than the facility: the page then says times are the facility's. */
  otherZone: boolean;
  calendars: { id: string; name: string }[];
  /** The doctor or resource shown; null shows every column (day and list only). */
  filter: string | null;
  day: DayLayout | null;
  /** Today's rows before now, folded into one row (MDV-P1-097); null when nothing is folded. */
  earlier: Earlier | null;
  agenda: { column: ScheduleColumn; entries: AgendaEntry[] }[];
  week: WeekDay[] | null;
  weekName: string;
  weekCalendar: string | null;
  /** Current instant (ms), ticking each minute: a free slot before it has started and is not offered. */
  now: number;
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
  /** Book or pick an empty slot, open a visit (MDV-P1-024), drop a dragged visit on a slot; `moving` names the patient being moved. */
  actions: GridActions;
  /** Opens booking at the next free slot (the pinned "+ Book" on phones); absent when the role may not book or nothing is free. */
  onBookNext?: () => void;
  /** The booking panel (or its hint), beside the schedule on wide screens and above it on narrow ones. */
  panel?: ReactNode;
  /** A booking or visit is open: on a tablet (768-1279 px) the panel is a drawer over the schedule, closed by its scrim or Escape. */
  onClosePanel?: () => void;
  /** 768-1279 px: where `onClosePanel` makes the panel a drawer, which then holds the keyboard focus. */
  tablet: boolean;
  /** Leave and exceptions (MDV-P1-012); absent when the role may not book. */
  leavePath?: string | null;
  /** Day sheet (MDV-P1-050): the audited paper fields once fetched, shown for printing only. */
  sheet: DaySheet | null;
  printedBy: string;
  printing: boolean;
  printFailed: boolean;
  onPrintSheet: () => void;
};

const all = '__all';

/** P-04 Schedule (MDV-P1-011, 021, 023): day, week and list views; times in the facility's zone. */
export default function SchedulePage(props: Props) {
  const { t, formatDay, formatShortDay, dir } = useI18n();
  const panelRef = useRef<HTMLDivElement>(null);
  const { date, view, weekSpan, onClosePanel } = props;
  const drawer = !!onClosePanel && props.tablet;
  const isWeek = view === 'week';
  // The page is "Schedule"; the day or week shown is secondary text beside it (MDV-P2-059), so the title never changes while a day loads.
  const shown = isWeek && weekSpan ? t('schedule.weekRange', { from: formatShortDay(weekSpan.from), to: formatShortDay(weekSpan.to) })
    : date ? formatDay(date) : undefined;
  return (
    <>
    {props.sheet && <DaySheetView sheet={props.sheet} printedBy={props.printedBy} />}
    {/* While a day sheet prints, the screen content stays off the paper. */}
    {/* Tighter chrome, so at 1024 by 768 the day starts in the top 240 px (MDV-P1-097). */}
    <PageContainer className={cn('gap-2 max-md:pb-16', props.sheet && 'print:hidden')}>
      <PageHeader title={t('nav.schedule')} meta={shown} description={props.timeZone && props.otherZone && t('schedule.facilityTime', { zone: props.timeZone })} />
      {/* The day's controls are a toolbar under the header, not its actions: nine of them do not fit beside the title, and the title row stays
          one 44 px row (MDV-P2-059). Printing (the list view is the paper agenda) keeps the heading and the schedule, not the controls. */}
      <div className="flex flex-wrap items-center gap-2 print:hidden">
        <Button variant="outline" size="icon" aria-label={t(isWeek ? 'schedule.prevWeek' : 'schedule.prevDay')} disabled={!date} onClick={props.onPrevious}>
          <ChevronLeft className="rtl:rotate-180" aria-hidden />
        </Button>
        <Button variant="outline" onClick={props.onToday}>{t('schedule.today')}</Button>
        <Button variant="outline" size="icon" aria-label={t(isWeek ? 'schedule.nextWeek' : 'schedule.nextDay')} disabled={!date} onClick={props.onNext}>
          <ChevronRight className="rtl:rotate-180" aria-hidden />
        </Button>
        <SegmentedControl label={t('schedule.views')} value={view} onValueChange={props.onView} options={scheduleViews.map(v => ({ value: v, label: t(`schedule.view.${v}`) }))} />
        {props.calendars.length > 1 && (
          <Select value={props.filter ?? all} onValueChange={v => props.onFilter(v === all ? null : v)}>
            <SelectTrigger aria-label={t('schedule.filter')} className="w-56"><SelectValue /></SelectTrigger>
            <SelectContent>
              {!isWeek && <SelectItem value={all}>{t('schedule.filter.all')}</SelectItem>}
              {props.calendars.map(c => <SelectItem key={c.id} value={c.id}>{c.name || t('schedule.doctor')}</SelectItem>)}
            </SelectContent>
          </Select>
        )}
        {/* Below 1280 px Print and Leave move into a More menu and the legend behind a Key button, so the toolbar stays one row (MDV-P1-097). */}
        <Button variant="outline" className="max-lg:hidden" disabled={!date || props.printing} onClick={props.onPrintSheet}><Printer aria-hidden />{t('schedule.sheet.print')}</Button>
        {props.leavePath && <Link to={props.leavePath} className={buttonVariants({ variant: 'outline', className: 'max-lg:hidden' })}><CalendarOff aria-hidden />{t('schedule.leave')}</Link>}
        <DropdownMenu dir={dir} modal={false}>
          <DropdownMenuTrigger asChild>
            <Button variant="outline" size="icon" className="lg:hidden" aria-label={t('nav.more')}><Ellipsis aria-hidden /></Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem disabled={!date || props.printing} onSelect={props.onPrintSheet}><Printer aria-hidden />{t('schedule.sheet.print')}</DropdownMenuItem>
            {props.leavePath && <DropdownMenuItem asChild><Link to={props.leavePath}><CalendarOff aria-hidden />{t('schedule.leave')}</Link></DropdownMenuItem>}
          </DropdownMenuContent>
        </DropdownMenu>
        {/* A disclosure, so it states collapsed or expanded itself; opened, the legend takes its own line under the controls. */}
        <details className="open:basis-full lg:hidden">
          <summary className={cn(buttonVariants({ variant: 'outline' }), 'w-fit cursor-pointer list-none [&::-webkit-details-marker]:hidden')}><Info aria-hidden />{t('schedule.key')}</summary>
          <Legend className="pt-2" />
        </details>
      </div>
      {props.printFailed && <p role="alert" className="text-sm font-bold text-destructive print:hidden">{t('schedule.sheet.failed')}</p>}
      <Legend className="max-lg:hidden" />
      {/* The schedule takes the full width when no booking panel sits beside it (the week view). */}
      {/* With one doctor shown, an open drawer leaves the column beside it rather than over it (MDV-P1-097). */}
      <div className={cn('grid grid-cols-[minmax(0,1fr)] items-start gap-5', props.panel && 'lg:grid-cols-[minmax(0,1fr)_340px]', drawer && view === 'day' && props.day?.columns.length === 1 && 'pe-96')}>
        <Body {...props} />
        {/* Only an open booking or visit is a drawer on a tablet; the hint and the "booked" note sit above the schedule, as on phones. */}
        {props.panel && onClosePanel && <div aria-hidden className="fixed inset-0 z-30 hidden bg-black/40 md:max-lg:block print:hidden" onClick={onClosePanel} />}
        {/* Below 1280 px the "select an empty slot" hint is not shown: the free slots say Tap to book (MDV-P1-097). */}
        {props.panel && <div ref={panelRef} {...(drawer && { role: 'dialog', 'aria-modal': true, 'aria-label': t('schedule.drawer') })} className={cn('scroll-mt-20 max-lg:order-first max-lg:has-[[data-hint]]:hidden lg:sticky lg:top-20 lg:max-h-[calc(100dvh-6rem)] lg:overflow-y-auto print:hidden',
          onClosePanel && 'md:max-lg:fixed md:max-lg:inset-y-0 md:max-lg:end-0 md:max-lg:z-30 md:max-lg:w-96 md:max-lg:overflow-y-auto md:max-lg:bg-background md:max-lg:p-3 md:max-lg:shadow-xl')}
          // Escape closes the drawer, not the side column on a wide screen; a portalled menu inside (a Select) is not in this element and closes itself.
          onKeyDown={e => { if (e.key === 'Escape' && drawer && e.currentTarget.contains(e.target as Node)) onClosePanel(); }}>
          {/* As a drawer it is modal: focus moves in, Tab stays inside, and closing hands focus back to what opened it (Radix, as in Dialog). */}
          {drawer ? <FocusScope trapped loop>{props.panel}</FocusScope> : props.panel}
        </div>}
      </div>
      {props.onBookNext && (
        // Pinned above the phone's bottom tabs; the panel it opens sits above the list, so it is brought into view.
        <Button className="fixed end-4 bottom-20 z-20 shadow-lg md:hidden print:hidden"
          // Rendered at once, so the panel, hidden while it was only the hint, is shown before it is scrolled to.
          onClick={() => { flushSync(() => props.onBookNext?.()); panelRef.current?.scrollIntoView({ block: 'start' }); }}>
          <Plus aria-hidden />{t('schedule.bookNext')}
        </Button>
      )}
    </PageContainer>
    </>
  );
}

// Offline is not handled here: the shell's offline strip says it once for every screen.
function Body(p: Props) {
  const { t } = useI18n();
  if (p.forbidden) return <PageMessage kind="denied" title={t('err.403')}>{t('schedule.forbidden.body')}</PageMessage>;
  if (p.failed)
    return <PageMessage kind="error" title={t('schedule.error.title')} action={<Button onClick={p.onRetry}>{t('schedule.retry')}</Button>}>{t('schedule.error.body')}</PageMessage>;
  // The day grid's box while it loads (MDV-P2-056), so the page keeps its height when the day comes.
  if (p.loading || !p.day || !p.timeZone) return <SkeletonGroup label={t('schedule.loading')}><Skeleton className="h-[calc(100dvh-12rem)] min-h-80 rounded-xl" /></SkeletonGroup>;
  if (p.day.columns.length === 0 && p.calendars.length === 0) return <EmptyState title={t('schedule.empty.title')}>{t('schedule.empty.body')}</EmptyState>;
  if (p.view === 'week')
    return p.week && p.weekCalendar
      ? <WeekView name={p.weekName} calendarId={p.weekCalendar} days={p.week} timeZone={p.timeZone} updating={p.updating} onOpenDay={p.onOpenDay} onOpen={p.actions.onOpen} />
      : <PageMessage kind="busy">{t('schedule.loading')}</PageMessage>;
  if (p.view === 'list') return <AgendaView sections={p.agenda} timeZone={p.timeZone} updating={p.updating} now={p.now} onSlot={p.actions.onSlot} onOpen={p.actions.onOpen} moving={p.actions.moving} />;
  return <DayGrid day={p.day} updating={p.updating} now={p.now} earlier={p.earlier} {...p.actions} />;
}
