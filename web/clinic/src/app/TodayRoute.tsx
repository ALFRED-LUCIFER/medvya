import DoctorTodayPage from '../features/today/components/DoctorTodayPage';
import { useTodayController } from '../features/today/hooks/useTodayController';
import { Consult } from './FrontDeskRoute';

/** Composition only: the doctor's Today (D-01, MDV-P2-002) for the active facility; "Start consultation" is the board's. */
export default function TodayRoute({ facilityId, online, userId }: { facilityId: string; online: boolean; userId: string }) {
  return <DoctorTodayPage {...useTodayController(facilityId, online, userId)} chartPath={id => `/patients/${id}`} consultPath={id => `/consult/${id}`} consultAction={card => <Consult key={card.id} card={card} online={online} />} />;
}
