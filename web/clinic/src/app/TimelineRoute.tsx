import { useParams } from 'react-router';
import { usePatient360Controller } from '../features/patient360/hooks/usePatient360Controller';
import TimelinePage from '../features/timeline/components/TimelinePage';
import { useTimelineController } from '../features/timeline/hooks/useTimelineController';

/** Composition only: one patient's timeline (MDV-P2-005, E-02) with the identity banner above it; the server checks clinical access and audits every call. */
export default function TimelineRoute() {
  const { patientId = '' } = useParams();
  return <Timeline key={patientId} patientId={patientId} />;
}

function Timeline({ patientId }: { patientId: string }) {
  return <TimelinePage {...useTimelineController(patientId)} patient={usePatient360Controller(patientId).data} backPath={`/patients/${patientId}`} />;
}
