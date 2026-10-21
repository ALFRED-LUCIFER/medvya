import SchedulePage from '../features/schedule/components/SchedulePage';
import { useScheduleController } from '../features/schedule/hooks/useScheduleController';

/** Composition only: the schedule controller for the active facility, fed to the presentational page. */
export default function ScheduleRoute({ facilityId, online }: { facilityId: string; online: boolean }) {
  return <SchedulePage {...useScheduleController(facilityId, online)} />;
}
