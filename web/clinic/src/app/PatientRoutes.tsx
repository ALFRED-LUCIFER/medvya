import { useParams, useSearchParams } from 'react-router';
import type { Country } from '@medvya/i18n';
import CompleteRegistration from '../features/registration/components/CompleteRegistration';
import Registration from '../features/registration/components/Registration';
import { useCompletionController } from '../features/registration/hooks/useCompletionController';
import { useRegistrationController } from '../features/registration/hooks/useRegistrationController';
import PatientDirectory from '../features/patients/components/PatientDirectory';
import PatientRecord from '../features/patients/components/PatientRecord';
import { usePatientDirectoryController } from '../features/patients/hooks/usePatientDirectoryController';
import { usePatientRecordController } from '../features/patients/hooks/usePatientRecordController';
import { patientDirectory } from '../features/shell/domain/navigation';

// Composition only: controller → component for the patient directory routes (MDV-P1-018).
const recordPath = (id: string) => `${patientDirectory.path}/${id}`;

export function PatientDirectoryRoute({ registerPath }: { registerPath: string | null }) {
  return <PatientDirectory {...usePatientDirectoryController()} registerPath={registerPath} recordPath={recordPath} />;
}

const completePath = (id: string) => `${recordPath(id)}/complete`;

// Revealing numbers and completing a registration both need Register / book patients at Full (the server checks the same).
function Record({ patientId, canReveal }: { patientId: string; canReveal: boolean }) {
  return <PatientRecord {...usePatientRecordController(patientId)} backPath={patientDirectory.path} canReveal={canReveal}
    completePath={canReveal ? completePath(patientId) : null} />;
}

type RegistrationLinks = { country: Country; actorName: string; bookPath: string | null; checkInPath: string | null };

/** ?mode=quick opens quick registration (the busy desk, walk-ins); without it, the full flow. The mode is not patient data. */
export function RegistrationRoute(links: RegistrationLinks) {
  const [params] = useSearchParams();
  const quick = params.get('mode') === 'quick';
  return <Registration {...useRegistrationController(links.country, quick)} {...links} recordPath={recordPath} quickPath="?mode=quick" fullPath="?mode=full" />;
}

export function PatientRecordRoute({ canReveal }: { canReveal: boolean }) {
  const { patientId = '' } = useParams();
  return <Record key={patientId} patientId={patientId} canReveal={canReveal} />;
}

function Completion({ patientId, country }: { patientId: string; country: Country }) {
  return <CompleteRegistration {...useCompletionController(patientId, country)} recordPath={recordPath(patientId)} />;
}

export function CompleteRegistrationRoute({ country }: { country: Country }) {
  const { patientId = '' } = useParams();
  return <Completion key={patientId} patientId={patientId} country={country} />;
}
