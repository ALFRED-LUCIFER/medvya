import { cn } from '@medvya/ui';
import type { Appointment } from '../domain/schedule';
import { AppointmentChip } from './AppointmentChip';
import { dragType } from './dragDrop';

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
