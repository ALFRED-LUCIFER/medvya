import ClinicTodayPage from '../features/clinic-today/components/ClinicTodayPage';
import { useClinicTodayController } from '../features/clinic-today/hooks/useClinicTodayController';

type Paths = { myDay: string | null; directory: string | null; waitingRoom: string | null };

/** Composition only: the owner's Clinic today (MDV-P2-053) of the active facility; each needs-attention line opens a screen this person may open. */
export default function ClinicTodayRoute({ facilityId, userId, paths }: { facilityId: string; userId: string; paths: Paths }) {
  const vm = useClinicTodayController(facilityId, userId, { myDay: !!paths.myDay, directory: !!paths.directory, waitingRoom: !!paths.waitingRoom });
  return <ClinicTodayPage {...vm} paths={{ ...paths, directory: paths.directory && `${paths.directory}?filter=Incomplete` }} />;
}
