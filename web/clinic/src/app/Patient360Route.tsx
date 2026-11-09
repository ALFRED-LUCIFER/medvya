import { useParams } from 'react-router';
import OpenReferrals from '../features/referrals/components/OpenReferrals';
import { useOpenReferralsController } from '../features/referrals/hooks/useReferralsController';
import Patient360Page from '../features/patient360/components/Patient360Page';
import { usePatient360Controller } from '../features/patient360/hooks/usePatient360Controller';

/** Composition only: one patient's chart (MDV-P2-004) by id in the address; the server checks clinical access and audits every open. */
export default function Patient360Route({ backPath }: { backPath: string }) {
  const { patientId = '' } = useParams();
  return <Chart key={patientId} patientId={patientId} backPath={backPath} />;
}

function Chart({ patientId, backPath }: { patientId: string; backPath: string }) {
  const referrals = useOpenReferralsController(patientId);
  return (
    <div className="flex flex-col gap-4">
      <Patient360Page {...usePatient360Controller(patientId)} backPath={backPath} consultPath={id => `/consult/${id}`} timelinePath={`/patients/${patientId}/timeline`} documentsPath={`/patients/${patientId}/docs`} />
      {!referrals.hidden && !referrals.loading && !referrals.failed && <OpenReferrals open={referrals.open} />}
    </div>
  );
}
