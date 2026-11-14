import { Outlet, useNavigate, useParams, useSearchParams } from 'react-router';
import type { Country } from '@medvya/i18n';
import CompleteRegistration from '../features/registration/components/CompleteRegistration';
import Registration from '../features/registration/components/Registration';
import { useCompletionController } from '../features/registration/hooks/useCompletionController';
import { useRegistrationController } from '../features/registration/hooks/useRegistrationController';
import PatientDirectory from '../features/patients/components/PatientDirectory';
import DeskVisits from '../features/visits/components/DeskVisits';
import { useDeskVisitsController } from '../features/visits/hooks/useVisitsController';
import PatientRecord from '../features/patients/components/PatientRecord';
import RecordTabs from '../features/patients/components/RecordTabs';
import { recordTabs, type RecordAccess } from '../features/patients/domain/recordTabs';
import { asFilter } from '../features/patients/domain/directory';
import MergeReview from '../features/merge/components/MergeReview';
import { useMergeController, useMergeHistory, useMergeRequest } from '../features/merge/hooks/useMergeController';
import { usePatientDirectoryController } from '../features/patients/hooks/usePatientDirectoryController';
import { usePatientConsentsController } from '../features/patients/hooks/usePatientConsentsController';
import { usePatientRecordController } from '../features/patients/hooks/usePatientRecordController';
import { patientDirectory } from '../features/shell/domain/navigation';
import { pickSlot } from '../features/schedule/domain/booking';
import { FollowUpDismissDialog, FollowUpDueLine } from '../features/front-desk/components/FollowUpDue';
import { bookFollowUpPath } from '../features/front-desk/domain/followUpDue';
import { useFollowUpDueController } from '../features/front-desk/hooks/useFollowUpDueController';

// Composition only: controller → component for the patient directory routes (MDV-P1-018).
const recordPath = (id: string) => `${patientDirectory.path}/${id}`;

/** A row opens the Patient record on the first tab this role may open: Summary for clinicians, Details for the desk (MDV-P2-051). */
export function PatientDirectoryRoute({ registerPath, mergePath, access }: { registerPath: string | null; mergePath: string | null; access: RecordAccess }) {
  const [params] = useSearchParams();
  return <PatientDirectory {...usePatientDirectoryController(asFilter(params.get('filter')))} registerPath={registerPath} mergePath={mergePath} recordPath={id => recordTabs(id, access)[0]?.path ?? recordPath(id)} />;
}

/** The one Patient record (MDV-P2-051): its tabs above whichever tab is open; each tab's route and the server decide access as before. */
export function RecordLayout({ access }: { access: RecordAccess }) {
  const { patientId = '' } = useParams();
  return <div className="flex flex-col gap-4"><RecordTabs tabs={recordTabs(patientId, access)} /><Outlet /></div>;
}

const mergePath = `${patientDirectory.path}/merge`;

/** P-03: each step is gated by its own capability here and again by the server (Register/book Full, Clinical notes Sign, Staff Config). */
export function MergeReviewRoute({ can }: { can: { request: boolean; approve: boolean; undo: boolean } }) {
  const { requestId = null } = useParams();
  return <MergeReview {...useMergeController(requestId)} can={can} backPath={patientDirectory.path} reviewPath={id => `${mergePath}/${id}`} />;
}

const completePath = (id: string) => `${recordPath(id)}/complete`;

// Revealing numbers and completing a registration both need Register / book patients at Full (the server checks the same).
function Record({ patientId, canReveal, facilityId, timeZone, bookPath, checkInPath }: { patientId: string; canReveal: boolean; facilityId: string; timeZone?: string; bookPath: string | null; checkInPath: string | null }) {
  const navigate = useNavigate();
  const mergeRequest = useMergeRequest(patientId, id => void navigate(`${mergePath}/${id}`));
  const record = usePatientRecordController(patientId);
  const desk = useDeskVisitsController(facilityId, patientId);
  const dismissal = useFollowUpDueController();
  const online = true; // as the record's other actions: offline, the shell's banner says so and a failed dismiss says so in the dialog
  // MDV-P1-102: the doctor's follow-up on the desk record; Book and Dismiss need booking access (the server checks again).
  const book = canReveal ? bookPath : null;
  const followUp = desk.followUp && <>
    <FollowUpDueLine followUp={desk.followUp} patientName={record.patient?.fullName ?? ''} online={online}
      bookPath={book && bookFollowUpPath(book, patientId, desk.followUp)} onDismiss={book ? dismissal.onStart : null} />
    <FollowUpDismissDialog {...dismissal} online={online} />
  </>;
  return <PatientRecord {...record} merges={useMergeHistory(patientId)} consents={usePatientConsentsController(patientId)} backPath={patientDirectory.path} canReveal={canReveal}
    completePath={canReveal ? completePath(patientId) : null}
    actions={{ bookPath: canReveal && bookPath ? `${bookPath}?patient=${patientId}` : null, checkInPath: checkInPath ? `${checkInPath}?patient=${patientId}` : null, editPath: canReveal ? completePath(patientId) : null }} visits={<DeskVisits {...desk} followUp={followUp} timeZone={timeZone} />} mergeRequest={canReveal ? mergeRequest : null} />;
}

type RegistrationLinks = { country: Country; scope: string; actorName: string; timeZone?: string; bookPath: string | null; checkInPath: string | null };

/**
 * ?mode=quick opens quick registration (the busy desk, walk-ins); without it, the full flow. The mode is not patient data.
 * Opened from the booking panel, the address also names the slot (MDV-P1-021); switching mode keeps it, and success goes back to it.
 */
export function RegistrationRoute(links: RegistrationLinks) {
  const [params] = useSearchParams();
  const quick = params.get('mode') === 'quick';
  const slot = pickSlot(params);
  const withMode = (mode: string) => `?${new URLSearchParams([...(slot ?? []), ['mode', mode]])}`;
  return <Registration {...useRegistrationController(links.country, quick, links.scope)} {...links} recordPath={recordPath} quickPath={withMode('quick')} fullPath={withMode('full')}
    slot={slot?.toString() ?? null} />;
}

export function PatientRecordRoute({ canReveal, facilityId, timeZone, bookPath, checkInPath }: { canReveal: boolean; facilityId: string; timeZone?: string; bookPath: string | null; checkInPath: string | null }) {
  const { patientId = '' } = useParams();
  return <Record key={patientId} patientId={patientId} canReveal={canReveal} facilityId={facilityId} timeZone={timeZone} bookPath={bookPath} checkInPath={checkInPath} />;
}

function Completion({ patientId, country }: { patientId: string; country: Country }) {
  return <CompleteRegistration {...useCompletionController(patientId, country)} recordPath={recordPath(patientId)} />;
}

export function CompleteRegistrationRoute({ country }: { country: Country }) {
  const { patientId = '' } = useParams();
  return <Completion key={patientId} patientId={patientId} country={country} />;
}
