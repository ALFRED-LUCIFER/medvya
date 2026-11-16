import { useParams } from 'react-router';
import { usePatient360Controller } from '../features/patient360/hooks/usePatient360Controller';
import TimelinePage from '../features/timeline/components/TimelinePage';
import { useTimelineController } from '../features/timeline/hooks/useTimelineController';

/** Composition only: one patient's timeline (MDV-P2-005, E-02) with the identity banner above it; the server checks clinical access and audits every call. */
export default function TimelineRoute({ printRx, userId }: { printRx: boolean; userId: string }) {
  const { patientId = '' } = useParams();
  return <Timeline key={patientId} patientId={patientId} printRx={printRx} userId={userId} />;
}

function Timeline({ patientId, printRx, userId }: { patientId: string; printRx: boolean; userId: string }) {
  return <TimelinePage {...useTimelineController(patientId, userId)} patient={usePatient360Controller(patientId).data} consultPath={id => `/consult/${id}`} printRx={printRx} />;
}
