import SchedulePage from '../features/schedule/components/SchedulePage';
import { useScheduleController } from '../features/schedule/hooks/useScheduleController';

/** Composition only: the schedule controller for the active facility, fed to the presentational page. */
export default function ScheduleRoute({ facilityId }: { facilityId: string }) {
  return <SchedulePage {...useScheduleController(facilityId)} />;
}
