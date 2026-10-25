import AppointmentPanel from '../features/schedule/components/AppointmentPanel';
import BookingPanel from '../features/schedule/components/BookingPanel';
import SchedulePage, { type GridActions } from '../features/schedule/components/SchedulePage';
import { useAppointmentController } from '../features/schedule/hooks/useAppointmentController';
import { useBookingController } from '../features/schedule/hooks/useBookingController';
import { useScheduleController } from '../features/schedule/hooks/useScheduleController';

type Props = { facilityId: string; userId: string; canBook: boolean; canCheckIn: boolean; registerPath: string | null };

/**
 * Composition only: schedule, booking and appointment controllers for the active facility, fed to the presentational page and
 * panels. One panel at a time: an opened visit (MDV-P1-024) or booking (MDV-P1-021); a slot click books, or picks the new slot
 * while a visit is being rescheduled.
 */
export default function ScheduleRoute({ facilityId, userId, canBook, canCheckIn, registerPath }: Props) {
  const schedule = useScheduleController(facilityId, userId);
  const booking = useBookingController(facilityId);
  const visit = useAppointmentController(facilityId, { timeZone: schedule.timeZone, canBook, canCheckIn });
  const picking = visit.mode === 'pick' || visit.mode === 'confirmMove';
  const column = schedule.day?.columns.find(c => c.calendarId === booking.choice?.calendarId);

  const book = (calendarId: string, name: string, start: string) => { visit.onClose(); booking.onSlot(calendarId, name, start); };
  const actions: GridActions = {
    onSlot: picking ? visit.onTarget : canBook ? book : undefined,
    onOpen: (appointment, calendarId, calendarName) => { booking.onClose(); visit.onOpen(appointment, calendarId, calendarName); },
    onDrop: canBook ? (appointment, from, to) => {
      booking.onClose();
      visit.onOpen(appointment, from.calendarId, from.calendarName);
      visit.onTarget(to.calendarId, to.calendarName, to.start);
    } : undefined,
    moving: picking && visit.opened
      ? { patient: visit.opened.appointment.patientName, kind: schedule.day?.columns.find(c => c.calendarId === visit.opened!.calendarId)?.calendarKind ?? 'Doctor' }
      : undefined,
  };

  const opened = visit.opened;
  const panel = opened && visit.rules && schedule.timeZone
    ? <AppointmentPanel {...visit} opened={opened} rules={visit.rules} timeZone={schedule.timeZone}
        onRebook={canBook ? () => { booking.onRebook({ id: opened.appointment.patientId, name: opened.appointment.patientName }); visit.onClose(); } : undefined} />
    // The week has no slots to pick, so the booking panel shows there only while a booking is under way.
    : canBook && schedule.timeZone && (schedule.view !== 'week' || booking.choice || booking.booked)
    ? <BookingPanel {...booking} registerPath={registerPath} timeZone={schedule.timeZone} visitLengths={column?.visitLengths ?? []} />
    : undefined;
  const next = schedule.nextFree;
  return (
    <SchedulePage {...schedule} panel={panel} actions={actions}
      // Hidden while a booking or a change is under way, so it never covers the panel's fields.
      onBookNext={canBook && next && !booking.choice && !opened ? () => book(next.column.calendarId, next.column.name, next.slot.start) : undefined} />
  );
}
