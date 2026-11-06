import { useSearchParams } from 'react-router';
import AppointmentPanel from '../features/schedule/components/AppointmentPanel';
import BookingPanel from '../features/schedule/components/BookingPanel';
import SchedulePage, { type GridActions } from '../features/schedule/components/SchedulePage';
import { useAppointmentController } from '../features/schedule/hooks/useAppointmentController';
import { useBookingController } from '../features/schedule/hooks/useBookingController';
import { useScheduleController } from '../features/schedule/hooks/useScheduleController';
import { readAddress, type ScheduleAddress } from '../features/schedule/domain/views';

type Props = { facilityId: string; userId: string; userName: string; canBook: boolean; canCheckIn: boolean; registerPath: string | null; leavePath: string | null };

/**
 * Composition only: schedule, booking and appointment controllers for the active facility, fed to the presentational page and
 * panels. One panel at a time: an opened visit (MDV-P1-024) or booking (MDV-P1-021); a slot click books, or picks the new slot
 * while a visit is being rescheduled.
 */
export default function ScheduleRoute({ facilityId, userId, userName, canBook, canCheckIn, registerPath, leavePath }: Props) {
  const [params, setParams] = useSearchParams();
  // Date and view in the address (?date=…&view=…); replace, so stepping through days does not fill the back button.
  const address = {
    ...readAddress(params),
    onChange: (next: (current: ScheduleAddress) => Partial<ScheduleAddress>) => setParams(p => {
      const current = readAddress(p), merged = { ...current, ...next(current) }, out = new URLSearchParams(p);
      for (const key of ['date', 'view'] as const) { const value = merged[key]; if (value) out.set(key, value); else out.delete(key); }
      return out;
    }, { replace: true }),
  };
  const schedule = useScheduleController(facilityId, userId, address);
  const booking = useBookingController(facilityId, schedule.timeZone);
  const visit = useAppointmentController(facilityId, { timeZone: schedule.timeZone, canBook, canCheckIn });
  const picking = visit.mode === 'pick' || visit.mode === 'confirmMove';
  // An opened visit belongs to the day and view it was opened on; a reschedule keeps it while the desk looks for the new slot.
  const leaving = <A extends unknown[]>(go: (...args: A) => void) => (...args: A) => { if (visit.opened && !picking) visit.onClose(); go(...args); };
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
    : canBook && schedule.timeZone && (schedule.view !== 'week' || booking.choice || booking.booked || booking.seriesDone)
    ? <BookingPanel {...booking} registerPath={registerPath} timeZone={schedule.timeZone} visitLengths={column?.visitLengths ?? []} />
    : undefined;
  const next = schedule.nextFree;
  return (
    <SchedulePage {...schedule} printedBy={userName} panel={panel} actions={actions} leavePath={leavePath}
      onPrevious={leaving(schedule.onPrevious)} onNext={leaving(schedule.onNext)} onToday={leaving(schedule.onToday)}
      onOpenDay={leaving(schedule.onOpenDay)} onView={leaving(schedule.onView)}
      // Hidden while a booking or a change is under way, so it never covers the panel's fields.
      onBookNext={canBook && next && !booking.choice && !opened ? () => book(next.column.calendarId, next.column.name, next.slot.start) : undefined} />
  );
}
