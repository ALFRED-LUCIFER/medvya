import BookingPanel from '../features/schedule/components/BookingPanel';
import SchedulePage from '../features/schedule/components/SchedulePage';
import { useBookingController } from '../features/schedule/hooks/useBookingController';
import { useScheduleController } from '../features/schedule/hooks/useScheduleController';

type Props = { facilityId: string; userId: string; canBook: boolean; registerPath: string | null };

/** Composition only: schedule and booking controllers for the active facility, fed to the presentational page and panel. */
export default function ScheduleRoute({ facilityId, userId, canBook, registerPath }: Props) {
  const schedule = useScheduleController(facilityId, userId);
  const booking = useBookingController(facilityId);
  const column = schedule.day?.columns.find(c => c.calendarId === booking.choice?.calendarId);
  // The week has no slots to pick, so the panel shows there only while a booking is under way.
  const panel = canBook && schedule.timeZone && (schedule.view !== 'week' || booking.choice || booking.booked)
    ? <BookingPanel {...booking} registerPath={registerPath} timeZone={schedule.timeZone} visitLengths={column?.visitLengths ?? []} />
    : undefined;
  const next = schedule.nextFree;
  return (
    <SchedulePage {...schedule} panel={panel}
      onSlot={canBook ? booking.onSlot : undefined}
      onBookNext={canBook && next ? () => booking.onSlot(next.column.calendarId, next.column.name, next.slot.start) : undefined} />
  );
}
