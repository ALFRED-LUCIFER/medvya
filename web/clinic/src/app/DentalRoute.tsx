import { useParams } from 'react-router';
import DentalPage from '../features/dental/components/DentalPage';
import { nextStageAddress } from '../features/dental/domain/dental';
import { useDentalController } from '../features/dental/hooks/useDentalController';
import { usePatient360Controller } from '../features/patient360/hooks/usePatient360Controller';

/** Composition only: one patient's dental chart and plan (MDV-P8-011, MDV-P8-012) by id in the address; the server checks clinical access and audits every open. */
export default function DentalRoute({ canChange, userId, schedulePath }: { canChange: boolean; userId: string; schedulePath: string | null }) {
  const { patientId = '' } = useParams();
  return <Dental key={patientId} patientId={patientId} canChange={canChange} userId={userId} schedulePath={schedulePath} />;
}

function Dental({ patientId, canChange, userId, schedulePath }: { patientId: string; canChange: boolean; userId: string; schedulePath: string | null }) {
  const dental = useDentalController(patientId, canChange);
  // MDV-P8-013: the next stage opens the schedule with the patient, this dentist, the visit's chair and the stage's length filled in.
  const bookStage = (minutes: number) => schedulePath && `${schedulePath}?${nextStageAddress(patientId, userId, minutes, dental.chart?.openVisitChairId ?? null)}`;
  // The banner's identity and allergy come from the chart, read once (audited) as the Timeline tab does.
  return <DentalPage {...dental} patient={usePatient360Controller(patientId).data} consultPath={dental.encounterId ? `/consult/${dental.encounterId}` : null} bookStage={bookStage} />;
}
