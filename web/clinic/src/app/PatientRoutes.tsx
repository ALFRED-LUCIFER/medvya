import { useNavigate, useParams, useSearchParams } from 'react-router';
import type { Country } from '@medvya/i18n';
import CompleteRegistration from '../features/registration/components/CompleteRegistration';
import Registration from '../features/registration/components/Registration';
import { useCompletionController } from '../features/registration/hooks/useCompletionController';
import { useRegistrationController } from '../features/registration/hooks/useRegistrationController';
import PatientDirectory from '../features/patients/components/PatientDirectory';
import DeskVisits from '../features/visits/components/DeskVisits';
import { useDeskVisitsController } from '../features/visits/hooks/useVisitsController';
import PatientRecord from '../features/patients/components/PatientRecord';
import MergeReview from '../features/merge/components/MergeReview';
import { useMergeController, useMergeHistory, useMergeRequest } from '../features/merge/hooks/useMergeController';
import { usePatientDirectoryController } from '../features/patients/hooks/usePatientDirectoryController';
import { usePatientConsentsController } from '../features/patients/hooks/usePatientConsentsController';
import { usePatientRecordController } from '../features/patients/hooks/usePatientRecordController';
import { patientDirectory } from '../features/shell/domain/navigation';
import { pickSlot } from '../features/schedule/domain/booking';

// Composition only: controller → component for the patient directory routes (MDV-P1-018).
const recordPath = (id: string) => `${patientDirectory.path}/${id}`;

export function PatientDirectoryRoute({ registerPath, mergePath }: { registerPath: string | null; mergePath: string | null }) {
  return <PatientDirectory {...usePatientDirectoryController()} registerPath={registerPath} mergePath={mergePath} recordPath={recordPath} />;
}

const mergePath = `${patientDirectory.path}/merge`;

/** P-03: each step is gated by its own capability here and again by the server (Register/book Full, Clinical notes Sign, Staff Config). */
export function MergeReviewRoute({ can }: { can: { request: boolean; approve: boolean; undo: boolean } }) {
  const { requestId = null } = useParams();
  return <MergeReview {...useMergeController(requestId)} can={can} backPath={patientDirectory.path} reviewPath={id => `${mergePath}/${id}`} />;
}

const completePath = (id: string) => `${recordPath(id)}/complete`;

// Revealing numbers and completing a registration both need Register / book patients at Full (the server checks the same).
function Record({ patientId, canReveal, canDocs, facilityId, timeZone, bookPath, checkInPath }: { patientId: string; canReveal: boolean; canDocs: boolean; facilityId: string; timeZone?: string; bookPath: string | null; checkInPath: string | null }) {
  const navigate = useNavigate();
  const mergeRequest = useMergeRequest(patientId, id => void navigate(`${mergePath}/${id}`));
  return <PatientRecord {...usePatientRecordController(patientId)} merges={useMergeHistory(patientId)} consents={usePatientConsentsController(patientId)} backPath={patientDirectory.path} canReveal={canReveal}
    completePath={canReveal ? completePath(patientId) : null}
    actions={{ bookPath: canReveal && bookPath ? `${bookPath}?patient=${patientId}` : null, checkInPath: checkInPath ? `${checkInPath}?patient=${patientId}` : null, editPath: canReveal ? completePath(patientId) : null }} documentsPath={canDocs ? `/patients/${patientId}/docs` : null} visits={<DeskVisits {...useDeskVisitsController(facilityId, patientId)} timeZone={timeZone} />} mergeRequest={canReveal ? mergeRequest : null} />;
}

type RegistrationLinks = { country: Country; actorName: string; timeZone?: string; bookPath: string | null; checkInPath: string | null };

/**
 * ?mode=quick opens quick registration (the busy desk, walk-ins); without it, the full flow. The mode is not patient data.
 * Opened from the booking panel, the address also names the slot (MDV-P1-021); switching mode keeps it, and success goes back to it.
 */
export function RegistrationRoute(links: RegistrationLinks) {
  const [params] = useSearchParams();
  const quick = params.get('mode') === 'quick';
  const slot = pickSlot(params);
  const withMode = (mode: string) => `?${new URLSearchParams([...(slot ?? []), ['mode', mode]])}`;
  return <Registration {...useRegistrationController(links.country, quick)} {...links} recordPath={recordPath} quickPath={withMode('quick')} fullPath={withMode('full')}
    slot={slot?.toString() ?? null} />;
}

export function PatientRecordRoute({ canReveal, canDocs, facilityId, timeZone, bookPath, checkInPath }: { canReveal: boolean; canDocs: boolean; facilityId: string; timeZone?: string; bookPath: string | null; checkInPath: string | null }) {
  const { patientId = '' } = useParams();
  return <Record key={patientId} patientId={patientId} canReveal={canReveal} canDocs={canDocs} facilityId={facilityId} timeZone={timeZone} bookPath={bookPath} checkInPath={checkInPath} />;
}

function Completion({ patientId, country }: { patientId: string; country: Country }) {
  return <CompleteRegistration {...useCompletionController(patientId, country)} recordPath={recordPath(patientId)} />;
}

export function CompleteRegistrationRoute({ country }: { country: Country }) {
  const { patientId = '' } = useParams();
  return <Completion key={patientId} patientId={patientId} country={country} />;
}
