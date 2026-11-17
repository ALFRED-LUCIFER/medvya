import { useSearchParams } from 'react-router';
import BrandsPanel from '../features/formulary/components/BrandsPanel';
import FormularyPanel from '../features/formulary/components/FormularyPanel';
import OrderSetsPanel from '../features/formulary/components/OrderSetsPanel';
import { useBrandsController, useFormularyController, useOrderSetsController } from '../features/formulary/hooks/useFormularyController';
import DoctorsPanel from '../features/settings/components/DoctorsPanel';
import HolidaysPanel from '../features/settings/components/HolidaysPanel';
import HoursPanel from '../features/settings/components/HoursPanel';
import LocationsPanel from '../features/settings/components/LocationsPanel';
import ProfilePanel from '../features/settings/components/ProfilePanel';
import DataPanel from '../features/settings/components/DataPanel';
import NotificationsPanel from '../features/settings/components/NotificationsPanel';
import NurseIntakePanel from '../features/settings/components/NurseIntakePanel';
import QueueRulePanel from '../features/settings/components/QueueRulePanel';
import TermsPanel from '../features/settings/components/TermsPanel';
import SettingsPage from '../features/settings/components/SettingsPage';
import BillingSettingsPanel from '../features/billing/components/BillingSettingsPanel';
import RazorpayPanel from '../features/billing/components/RazorpayPanel';
import ServicesPanel from '../features/billing/components/ServicesPanel';
import { useBillingSettingsController } from '../features/billing/hooks/useBillingSettingsController';
import { useRazorpayController } from '../features/billing/hooks/useRazorpayController';
import { useServicesController } from '../features/billing/hooks/useServicesController';
import { settingsGroups, visibleGroups, type SettingsTab } from '../features/settings/domain/settings';
import { useDoctorsController } from '../features/settings/hooks/useDoctorsController';
import { useHolidaysController } from '../features/settings/hooks/useHolidaysController';
import { useOpeningHoursController, useSchedulesController } from '../features/settings/hooks/useHoursController';
import { useLocationChoice } from '../features/settings/hooks/useLocationChoice';
import { useLocationsController } from '../features/settings/hooks/useLocationsController';
import { useNotificationSettingsController } from '../features/settings/hooks/useNotificationSettingsController';
import { useNurseIntakeController } from '../features/settings/hooks/useNurseIntakeController';
import { useQueueRuleController } from '../features/settings/hooks/useQueueRuleController';
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

// Each panel is its own component so its controller runs (and fetches) only while its tab is open.
const Profile = () => <ProfilePanel {...useProfileController()} />;
const Locations = ({ facilityId, country }: { facilityId: string; country: 'IN' | 'AE' | null }) => <LocationsPanel {...useLocationsController(facilityId, country)} />;
function Hours({ facilityId }: { facilityId: string }) {
  const choice = useLocationChoice(facilityId);
  return <HoursPanel choice={choice} hours={useOpeningHoursController(choice.locationId)} schedules={useSchedulesController(choice.locationId)} />;
}
function Holidays({ facilityId }: { facilityId: string }) {
  const choice = useLocationChoice(facilityId);
  const zones = new Map(choice.locations.map(l => [l.id, l.timeZone]));
  return <HolidaysPanel choice={choice} holidays={useHolidaysController(choice.locationId)} zoneOf={id => zones.get(id) ?? 'UTC'} />;
}
function Doctors({ facilityId, timeZone }: { facilityId: string; timeZone: string }) {
  return <DoctorsPanel {...useDoctorsController(facilityId)} locations={useLocationChoice(facilityId).locations} timeZone={timeZone} />;
}

function Medicines({ country }: { country: 'IN' | 'AE' | null }) {
  return (
    <>
      <FormularyPanel country={country} {...useFormularyController()} />
      <BrandsPanel {...useBrandsController()} />
      <OrderSetsPanel {...useOrderSetsController()} />
    </>
  );
}

const Terms = ({ timeZone }: { timeZone: string }) => <TermsPanel {...useTermsController()} timeZone={timeZone} />;
const Notifications = () => <NotificationsPanel {...useNotificationSettingsController()} />;
const Audit = () => <AuditLogPanel {...useAuditLogController()} />;
const Displays = ({ facilityId, timeZone }: { facilityId: string; timeZone: string }) => <DisplaysPage {...useDisplaysController(facilityId)} timeZone={timeZone} />;
const Metrics = ({ facilityId }: { facilityId: string }) => <PilotMetricsPanel {...usePilotMetricsController(facilityId)} />;

const Review = ({ timeZone }: { timeZone: string }) => <AccessReviewPanel {...useAccessReviewController()} timeZone={timeZone} />;
const AccessRequests = ({ timeZone }: { timeZone: string }) => <AccessRequestsPanel {...useAccessRequestsController()} timeZone={timeZone} />;
const ClinicalPolicy = () => <ClinicalPolicyPanel {...useClinicalPolicyController()} />;
const NurseIntake = () => <NurseIntakePanel {...useNurseIntakeController()} />;
const Queue = () => <QueueRulePanel {...useQueueRuleController()} />;
const BillingSettings = () => <BillingSettingsPanel {...useBillingSettingsController()} />;
const Razorpay = () => <RazorpayPanel {...useRazorpayController()} />;
const Services = ({ timeZone }: { timeZone: string }) => <ServicesPanel {...useServicesController(timeZone)} timeZone={timeZone} />;

function Staff({ facilityId, userId, timeZone }: { facilityId: string; userId: string; timeZone: string }) {
  const staff = useStaffController(facilityId);
  const roles = useCustomRolesController();
  return (
    <>
      <StaffPanel {...staff} currentUserId={userId} timeZone={timeZone} />
      {!staff.loading && !staff.failed && <CustomRolesPanel catalogue={staff.catalogue} {...roles} />}
    </>
  );
}

/** Composition only: Settings (F-08) with its tab in the address (?tab=hours), so a tab can be linked and survives a reload. */
export default function SettingsRoute({ facilityId, country, userId, timeZone, billing }: { facilityId: string; country: 'IN' | 'AE' | null; userId: string; timeZone: string; billing: boolean }) {
  const [params, setParams] = useSearchParams();
  const asked = params.get('tab');
  const groups = visibleGroups(billing);
  const tabs: readonly string[] = groups.flatMap(g => settingsGroups[g]);
  const tab: SettingsTab = tabs.includes(asked ?? '') ? (asked as SettingsTab) : 'profile';
  const panels = { profile: <Profile />, locations: <Locations facilityId={facilityId} country={country} />, hours: <Hours facilityId={facilityId} />, holidays: <Holidays facilityId={facilityId} />, staff: <Staff facilityId={facilityId} userId={userId} timeZone={timeZone} />, nurse: <NurseIntake />, queue: <Queue />, requests: <AccessRequests timeZone={timeZone} />, policy: <ClinicalPolicy />, review: <Review timeZone={timeZone} />, doctors: <Doctors facilityId={facilityId} timeZone={timeZone} />, medicines: <Medicines country={country} />, displays: <Displays facilityId={facilityId} timeZone={timeZone} />, audit: <Audit />, metrics: <Metrics facilityId={facilityId} />, terms: <Terms timeZone={timeZone} />, notifications: <Notifications />, data: <DataPanel country={country} accessLogPath="/settings?tab=audit" />, services: <Services timeZone={timeZone} />, billing: <><BillingSettings />{country === 'IN' && <Razorpay />}</> };
  return <SettingsPage tab={tab} onTab={next => setParams({ tab: next }, { replace: true })} panels={panels} groups={groups} />;
}
