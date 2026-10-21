import BookingPanel from '../features/schedule/components/BookingPanel';
import SchedulePage from '../features/schedule/components/SchedulePage';
import { useBookingController } from '../features/schedule/hooks/useBookingController';
import { useScheduleController } from '../features/schedule/hooks/useScheduleController';

type Props = { facilityId: string; online: boolean; canBook: boolean };

/** Composition only: schedule and booking controllers for the active facility, fed to the presentational page and panel. */
export default function ScheduleRoute({ facilityId, online, canBook }: Props) {
  const schedule = useScheduleController(facilityId, online);
  const booking = useBookingController(facilityId);
  const column = schedule.day?.columns.find(c => c.calendarId === booking.choice?.calendarId);
  const panel = canBook && schedule.day
    ? <BookingPanel {...booking} timeZone={schedule.day.timeZone} visitLengths={column?.visitLengths ?? []} />
    : undefined;
  return <SchedulePage {...schedule} onSlot={canBook ? booking.onSlot : undefined} panel={panel} />;
}
