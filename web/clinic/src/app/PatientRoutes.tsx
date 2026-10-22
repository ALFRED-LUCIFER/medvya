import { useParams } from 'react-router';
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

function Record({ patientId }: { patientId: string }) {
  return <PatientRecord {...usePatientRecordController(patientId)} backPath={patientDirectory.path} />;
}

export function PatientRecordRoute() {
  const { patientId = '' } = useParams();
  return <Record key={patientId} patientId={patientId} />;
}
