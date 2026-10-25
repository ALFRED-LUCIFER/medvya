import { useI18n } from '@medvya/i18n';
import { cn, scheduleLooks, type Look } from '@medvya/ui';
import type { Appointment, AppointmentState } from '../domain/schedule';
import { hatched, stateLook } from './looks';

/** State as icon and words (never colour alone): "Booked", "No-show" … */
export function StateLabel({ state }: { state: AppointmentState }) {
  const { t } = useI18n();
  const Icon = stateLook(state).icon;
  return (
    <span className="inline-flex items-center gap-1">
      <Icon aria-hidden className="size-3.5 shrink-0" />
      <strong className="font-extrabold">{t(`schedule.state.${state}`)}</strong>
    </span>
  );
}

/**
 * One appointment as a chip: patient, state, time and visit type, with the state's border (solid, dashed) as the shape cue.
 * In the day grid its height is the visit's length, so lines truncate; in the week and list they wrap.
 */
export function AppointmentChip({ appointment: a, time, wrap = false, className }: { appointment: Appointment; time: (iso: string) => string; wrap?: boolean; className?: string }) {
  const { t } = useI18n();
  const look = stateLook(a.state);
  return (
    // A span (displayed as a flex box) so the chip is valid inside the button that opens it.
    <span className={cn('flex flex-col gap-0.5 overflow-hidden rounded-lg px-2 py-1', className)} style={{ backgroundColor: look.bg, color: look.fg, border: look.border }}>
      <span className={cn('text-sm font-bold text-foreground', wrap ? 'break-words' : 'truncate')}>{a.patientName}</span>
      <span className={cn('flex items-center gap-x-1 text-xs', wrap ? 'flex-wrap' : 'truncate')}>
        <StateLabel state={a.state} />
        {t('schedule.appointment.detail', { time: time(a.start), type: t(`visitType.${a.visitType}`) })}
      </span>
    </span>
  );
}

/** Opens a visit's detail panel. */
export type OpenHandler = (appointment: Appointment, calendarId: string, calendarName: string) => void;
/** A booked visit dropped on an empty slot (drag to reschedule; the panel then asks before moving). */
export type DropHandler = (appointment: Appointment, from: { calendarId: string; calendarName: string }, to: { calendarId: string; calendarName: string; start: string }) => void;
/** What the drag carries: the visit's id only, never patient data. */
export const dragType = 'application/x-medvya-appointment';

/**
 * The chip as a button that opens the visit; a booked visit can also be dragged to a new slot when the role may book.
 * Dragging is never the only way: the panel's Reschedule picks a slot by click or keyboard (WCAG 2.5.7).
 */
export function OpenableChip({ appointment, time, wrap, className, onOpen, draggable }: {
  appointment: Appointment; time: (iso: string) => string; wrap?: boolean; className?: string; onOpen: () => void; draggable: boolean;
}) {
  return (
    <button type="button" onClick={onOpen} draggable={draggable || undefined}
      onDragStart={draggable ? e => { e.dataTransfer.setData(dragType, appointment.id); e.dataTransfer.effectAllowed = 'move'; } : undefined}
      className={cn('block w-full rounded-lg text-start focus-visible:outline-offset-2', draggable && 'cursor-grab', className)}>
      <AppointmentChip appointment={appointment} time={time} wrap={wrap} className="h-full" />
    </button>
  );
}

const legend = ['booked', 'checkedIn', 'inRoom', 'completed', 'noShow', 'cancelled', 'held'] as const;

/** What each look means, so states can be told apart by shape and words as well as colour. */
export function Legend() {
  const { t } = useI18n();
  return (
    <ul aria-label={t('schedule.legend')} className="flex flex-wrap gap-x-4 gap-y-1.5 text-xs text-muted-foreground">
      {legend.map(k => {
        const look: Look = scheduleLooks[k], Icon = look.icon;
        return (
          <li key={k} className="flex items-center gap-1.5">
            <span aria-hidden className="size-3.5 rounded-sm" style={{ backgroundColor: look.bg, border: look.border ?? `1px solid ${look.fg}` }} />
            <Icon aria-hidden className="size-3.5" />{t(look.label)}
          </li>
        );
      })}
      <li className="flex items-center gap-1.5">
        <span aria-hidden className={cn('size-3.5 rounded-sm border', hatched)} />
        <scheduleLooks.unavailable.icon aria-hidden className="size-3.5" />{t(scheduleLooks.unavailable.label)}
      </li>
    </ul>
  );
}
