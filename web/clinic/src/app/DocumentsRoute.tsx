import { useParams } from 'react-router';
import DocumentsPage from '../features/documents/components/DocumentsPage';
import { useDocumentsController } from '../features/documents/hooks/useDocumentsController';

/** Composition only: one patient's documents (MDV-P2-006), a tab of the Patient record; the server decides what each role may do (the desk uploads and lists, never opens). */
export default function DocumentsRoute() {
  const { patientId = '' } = useParams();
  return <Documents key={patientId} patientId={patientId} />;
}

function Documents({ patientId }: { patientId: string }) {
  return <DocumentsPage {...useDocumentsController(patientId)} />;
}
