import { useParams } from 'react-router';
import type { Country } from '@medvya/i18n';
import QuickRegistration from '../features/registration/components/QuickRegistration';
import { useQuickRegistrationController } from '../features/registration/hooks/useQuickRegistrationController';
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

export function QuickRegistrationRoute(links: RegistrationLinks) {
  return <QuickRegistration {...useQuickRegistrationController(links.country)} {...links} recordPath={recordPath} />;
}

export function PatientRecordRoute({ canReveal }: { canReveal: boolean }) {
  const { patientId = '' } = useParams();
  return <Record key={patientId} patientId={patientId} canReveal={canReveal} />;
}
