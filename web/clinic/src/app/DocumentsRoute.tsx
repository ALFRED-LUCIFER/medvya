import { useParams } from 'react-router';
import DocumentsPage from '../features/documents/components/DocumentsPage';
import { useDocumentsController } from '../features/documents/hooks/useDocumentsController';

/**
 * Composition only: one patient's documents (MDV-P2-006). Clinicians go back to the chart, the front desk to the patient record; the server
 * decides what each may do (the desk uploads and lists, never opens).
 */
export default function DocumentsRoute({ clinician, deskRecordPath }: { clinician: boolean; deskRecordPath: string | null }) {
  const { patientId = '' } = useParams();
  return <Documents key={patientId} patientId={patientId} backPath={clinician ? `/patients/${patientId}` : `${deskRecordPath ?? '/patients/directory'}/${patientId}`} />;
}

function Documents({ patientId, backPath }: { patientId: string; backPath: string }) {
  return <DocumentsPage {...useDocumentsController(patientId)} backPath={backPath} />;
}
