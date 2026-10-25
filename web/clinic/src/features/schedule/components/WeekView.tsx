import { useI18n } from '@medvya/i18n';
import { cn } from '@medvya/ui';
import type { WeekDay } from '../domain/views';
import { AppointmentChip, OpenableChip, type OpenHandler } from './parts';

type Props = { name: string; calendarId: string; days: WeekDay[]; timeZone: string; updating: boolean; onOpenDay: (date: string) => void; onOpen?: OpenHandler };

/**
 * Week view of one doctor or resource: per day the booked count against capacity and that day's appointments; a day opens its
 * day view. Seven columns from tablet width (scrolling inside its own focusable box when narrower); stacked days on phones.
 */
export default function WeekView({ name, calendarId, days, timeZone, updating, onOpenDay, onOpen }: Props) {
  const { t, formatTime, formatDay, formatShortDay } = useI18n();
  const time = (iso: string) => formatTime(new Date(iso), timeZone);
  return (
    <section aria-label={t('schedule.weekOf', { name })} aria-busy={updating || undefined} tabIndex={0}
      className={cn('overflow-x-auto rounded-lg border bg-card transition-opacity', updating && 'opacity-60')}>
      <ol className="grid md:min-w-[840px] md:grid-cols-7">
        {days.map(d => (
          <li key={d.date} className="flex flex-col gap-2 border-t p-3 first:border-t-0 md:min-h-80 md:border-t-0 md:border-s md:first:border-s-0">
            <h2 className="text-sm font-extrabold">
              {/* The visible short day is the whole accessible name (WCAG 2.5.3, label in name). */}
              <button type="button" className="min-h-11 text-start underline underline-offset-4 hover:text-action-600" onClick={() => onOpenDay(d.date)}>
                {formatShortDay(d.date)}
              </button>
            </h2>
            {d.working ? (
              <>
                <p className="text-sm"><span className="text-2xl font-bold tabular-nums">{d.booked}</span> {t('schedule.week.bookedOf', { capacity: d.capacity })}</p>
                <div aria-hidden className="h-1.5 overflow-hidden rounded-full bg-muted">
                  <div className="h-full bg-action-600" style={{ width: `${d.capacity ? Math.min(100, (100 * d.booked) / d.capacity) : 0}%` }} />
                </div>
              </>
            ) : (
              <p className="text-xs font-bold text-muted-foreground">{t('schedule.reason.NotWorking')}</p>
            )}
            {d.appointments.length > 0 && (
              <ul aria-label={t('schedule.week.appointments', { day: formatDay(d.date) })} className="flex flex-col gap-1.5">
                {d.appointments.map(a => <li key={a.id}>{onOpen
                  ? <OpenableChip appointment={a} time={time} wrap onOpen={() => onOpen(a, calendarId, name)} draggable={false} />
                  : <AppointmentChip appointment={a} time={time} wrap />}</li>)}
              </ul>
            )}
          </li>
        ))}
      </ol>
    </section>
  );
}
