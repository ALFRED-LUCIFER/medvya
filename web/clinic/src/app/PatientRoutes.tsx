import { useParams, useSearchParams } from 'react-router';
import type { Country } from '@medvya/i18n';
import Registration from '../features/registration/components/Registration';
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

function Record({ patientId, canReveal }: { patientId: string; canReveal: boolean }) {
  return <PatientRecord {...usePatientRecordController(patientId)} backPath={patientDirectory.path} canReveal={canReveal} />;
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
