import DoctorTodayPage from '../features/today/components/DoctorTodayPage';
import { useTodayController } from '../features/today/hooks/useTodayController';
import { Consult } from './FrontDeskRoute';

type Props = {
  facilityId: string; timeZone: string; online: boolean; userId: string;
  /** The doctor's memberships at the organisation's other locations, and the switch to one (#1365 F-061). */
  otherLocations: { id: string; facilityId: string; facilityName: string }[]; onSwitch: (membershipId: string) => void; switchingTo: string | null;
};

/** Composition only: the doctor's My day (D-01, MDV-P2-002) for the active facility; "Start consultation" is the board's. */
export default function TodayRoute({ facilityId, timeZone, online, userId, otherLocations, onSwitch, switchingTo }: Props) {
  return <DoctorTodayPage {...useTodayController(facilityId, timeZone, online, userId, otherLocations)} onSwitch={onSwitch} switchingTo={switchingTo} chartPath={id => `/patients/${id}`} consultPath={id => `/consult/${id}`} consultAction={(card, primary) => <Consult key={card.id} card={card} online={online} primary={primary} />} />;
}
