import DisplaysPage from '../features/queue-tv/components/DisplaysPage';
import { useDisplaysController } from '../features/queue-tv/hooks/useDisplaysController';

/** Composition only: the facility's queue displays (MDV-P1-031). */
export default function DisplaysRoute({ facilityId }: { facilityId: string }) {
  return <DisplaysPage {...useDisplaysController(facilityId)} />;
}
