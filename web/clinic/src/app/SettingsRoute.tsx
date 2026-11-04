import { useSearchParams } from 'react-router';
import HolidaysPanel from '../features/settings/components/HolidaysPanel';
import HoursPanel from '../features/settings/components/HoursPanel';
import LocationsPanel from '../features/settings/components/LocationsPanel';
import ProfilePanel from '../features/settings/components/ProfilePanel';
import SettingsPage from '../features/settings/components/SettingsPage';
import { settingsTabs, type SettingsTab } from '../features/settings/domain/settings';
import { useHolidaysController } from '../features/settings/hooks/useHolidaysController';
import { useOpeningHoursController, useSchedulesController } from '../features/settings/hooks/useHoursController';
import { useLocationChoice } from '../features/settings/hooks/useLocationChoice';
import { useLocationsController } from '../features/settings/hooks/useLocationsController';
import { useProfileController } from '../features/settings/hooks/useProfileController';

// Each panel is its own component so its controller runs (and fetches) only while its tab is open.
const Profile = () => <ProfilePanel {...useProfileController()} />;
const Locations = ({ facilityId }: { facilityId: string }) => <LocationsPanel {...useLocationsController(facilityId)} />;
function Hours({ facilityId }: { facilityId: string }) {
  const choice = useLocationChoice(facilityId);
  return <HoursPanel choice={choice} hours={useOpeningHoursController(choice.locationId)} schedules={useSchedulesController(choice.locationId)} />;
}
function Holidays({ facilityId }: { facilityId: string }) {
  const choice = useLocationChoice(facilityId);
  const zones = new Map(choice.locations.map(l => [l.id, l.timeZone]));
  return <HolidaysPanel choice={choice} holidays={useHolidaysController(choice.locationId)} zoneOf={id => zones.get(id) ?? 'UTC'} />;
}

/** Composition only: Settings (F-08) with its tab in the address (?tab=hours), so a tab can be linked and survives a reload. */
export default function SettingsRoute({ facilityId }: { facilityId: string }) {
  const [params, setParams] = useSearchParams();
  const asked = params.get('tab');
  const tab: SettingsTab = (settingsTabs as readonly string[]).includes(asked ?? '') ? (asked as SettingsTab) : 'profile';
  const panels = { profile: <Profile />, locations: <Locations facilityId={facilityId} />, hours: <Hours facilityId={facilityId} />, holidays: <Holidays facilityId={facilityId} /> };
  return <SettingsPage tab={tab} onTab={next => setParams({ tab: next }, { replace: true })} panels={panels} />;
}
