import WaitlistPage from '../features/waitlist/components/WaitlistPage';
import { useWaitlistController } from '../features/waitlist/hooks/useWaitlistController';

/** Composition only: the waitlist controller for the active facility, fed to the presentational page. */
export default function WaitlistRoute({ facilityId, boardPath }: { facilityId: string; boardPath: string }) {
  return <WaitlistPage {...useWaitlistController(facilityId)} boardPath={boardPath} />;
}
