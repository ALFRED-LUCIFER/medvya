import { useParams } from 'react-router';
import DentalPage from '../features/dental/components/DentalPage';
import { useDentalController } from '../features/dental/hooks/useDentalController';
import { usePatient360Controller } from '../features/patient360/hooks/usePatient360Controller';

/** Composition only: one patient's dental chart and plan (MDV-P8-011, MDV-P8-012) by id in the address; the server checks clinical access and audits every open. */
export default function DentalRoute({ canChange }: { canChange: boolean }) {
  const { patientId = '' } = useParams();
  return <Dental key={patientId} patientId={patientId} canChange={canChange} />;
}

function Dental({ patientId, canChange }: { patientId: string; canChange: boolean }) {
  const dental = useDentalController(patientId, canChange);
  // The banner's identity and allergy come from the chart, read once (audited) as the Timeline tab does.
  return <DentalPage {...dental} patient={usePatient360Controller(patientId).data} consultPath={dental.encounterId ? `/consult/${dental.encounterId}` : null} />;
}
