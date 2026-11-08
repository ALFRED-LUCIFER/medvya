import { useSearchParams } from 'react-router';
import DoctorsPanel from '../features/settings/components/DoctorsPanel';
import HolidaysPanel from '../features/settings/components/HolidaysPanel';
import HoursPanel from '../features/settings/components/HoursPanel';
import LocationsPanel from '../features/settings/components/LocationsPanel';
import ProfilePanel from '../features/settings/components/ProfilePanel';
import DataPanel from '../features/settings/components/DataPanel';
import NotificationsPanel from '../features/settings/components/NotificationsPanel';
import TermsPanel from '../features/settings/components/TermsPanel';
import SettingsPage from '../features/settings/components/SettingsPage';
import { settingsTabs, type SettingsTab } from '../features/settings/domain/settings';
import { useDoctorsController } from '../features/settings/hooks/useDoctorsController';
import { useHolidaysController } from '../features/settings/hooks/useHolidaysController';
import { useOpeningHoursController, useSchedulesController } from '../features/settings/hooks/useHoursController';
import { useLocationChoice } from '../features/settings/hooks/useLocationChoice';
import { useLocationsController } from '../features/settings/hooks/useLocationsController';
import { useNotificationSettingsController } from '../features/settings/hooks/useNotificationSettingsController';
import { useTermsController } from '../features/settings/hooks/useTermsController';
import { useProfileController } from '../features/settings/hooks/useProfileController';
import CustomRolesPanel from '../features/staff/components/CustomRolesPanel';
import AuditLogPanel from '../features/audit-log/components/AuditLogPanel';
import DisplaysPage from '../features/queue-tv/components/DisplaysPage';
import { useDisplaysController } from '../features/queue-tv/hooks/useDisplaysController';
import { useAuditLogController } from '../features/audit-log/hooks/useAuditLogController';
import PilotMetricsPanel from '../features/pilot-metrics/components/PilotMetricsPanel';
import { usePilotMetricsController } from '../features/pilot-metrics/hooks/usePilotMetricsController';
import AccessReviewPanel from '../features/staff/components/AccessReviewPanel';
import AccessRequestsPanel from '../features/clinical-access/components/AccessRequestsPanel';
import ClinicalPolicyPanel from '../features/clinical-access/components/ClinicalPolicyPanel';
import { useAccessRequestsController } from '../features/clinical-access/hooks/useAccessRequestsController';
import { useClinicalPolicyController } from '../features/clinical-access/hooks/useClinicalPolicyController';
import { useAccessReviewController } from '../features/staff/hooks/useAccessReviewController';
import StaffPanel from '../features/staff/components/StaffPanel';
import { useCustomRolesController } from '../features/staff/hooks/useCustomRolesController';
import { useStaffController } from '../features/staff/hooks/useStaffController';
import { useStaffTableController } from '../features/staff/hooks/useStaffTableController';

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
function Doctors({ facilityId }: { facilityId: string }) {
  return <DoctorsPanel {...useDoctorsController(facilityId)} locations={useLocationChoice(facilityId).locations} />;
}

const Terms = () => <TermsPanel {...useTermsController()} />;
const Notifications = () => <NotificationsPanel {...useNotificationSettingsController()} />;
const Audit = () => <AuditLogPanel {...useAuditLogController()} />;
const Displays = ({ facilityId }: { facilityId: string }) => <DisplaysPage {...useDisplaysController(facilityId)} />;
const Metrics = ({ facilityId }: { facilityId: string }) => <PilotMetricsPanel {...usePilotMetricsController(facilityId)} />;

const Review = () => <AccessReviewPanel {...useAccessReviewController()} />;
const AccessRequests = () => <AccessRequestsPanel {...useAccessRequestsController()} />;
const ClinicalPolicy = () => <ClinicalPolicyPanel {...useClinicalPolicyController()} />;

function Staff({ facilityId }: { facilityId: string }) {
  const staff = useStaffController(facilityId);
  const table = useStaffTableController(staff.staff);
  const roles = useCustomRolesController();
  return (
    <>
      <StaffPanel {...staff} {...table} />
      {!staff.loading && !staff.failed && <CustomRolesPanel catalogue={staff.catalogue} {...roles} />}
      <AccessRequests />
      <ClinicalPolicy />
      <Review />
    </>
  );
}

/** Composition only: Settings (F-08) with its tab in the address (?tab=hours), so a tab can be linked and survives a reload. */
export default function SettingsRoute({ facilityId, country }: { facilityId: string; country: 'IN' | 'AE' | null }) {
  const [params, setParams] = useSearchParams();
  const asked = params.get('tab');
  const tab: SettingsTab = (settingsTabs as readonly string[]).includes(asked ?? '') ? (asked as SettingsTab) : 'profile';
  const panels = { profile: <Profile />, locations: <Locations facilityId={facilityId} />, hours: <Hours facilityId={facilityId} />, holidays: <Holidays facilityId={facilityId} />, staff: <Staff facilityId={facilityId} />, doctors: <Doctors facilityId={facilityId} />, displays: <Displays facilityId={facilityId} />, audit: <Audit />, metrics: <Metrics facilityId={facilityId} />, terms: <Terms />, notifications: <Notifications />, data: <DataPanel country={country} accessLogPath="/settings?tab=audit" /> };
  return <SettingsPage tab={tab} onTab={next => setParams({ tab: next }, { replace: true })} panels={panels} />;
}
