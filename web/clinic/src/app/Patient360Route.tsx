import { useParams } from 'react-router';
import OpenReferrals from '../features/referrals/components/OpenReferrals';
import { useOpenReferralsController } from '../features/referrals/hooks/useReferralsController';
import HistorySections from '../features/patient360/components/HistorySections';
import Patient360Page from '../features/patient360/components/Patient360Page';
import { useHistoryController } from '../features/patient360/hooks/useHistoryController';
import { usePatient360Controller } from '../features/patient360/hooks/usePatient360Controller';

/** Composition only: one patient's chart (MDV-P2-004) by id in the address; the server checks clinical access and audits every open. */
export default function Patient360Route({ backPath, canEditHistory }: { backPath: string; canEditHistory: boolean }) {
  const { patientId = '' } = useParams();
  return <Chart key={patientId} patientId={patientId} backPath={backPath} canEditHistory={canEditHistory} />;
}

function Chart({ patientId, backPath, canEditHistory }: { patientId: string; backPath: string; canEditHistory: boolean }) {
  const referrals = useOpenReferralsController(patientId);
  const chart = usePatient360Controller(patientId);
  const history = useHistoryController(patientId, chart.data, canEditHistory);
  return (
    <div className="flex flex-col gap-4">
      <Patient360Page {...chart} backPath={backPath} consultPath={id => `/consult/${id}`} timelinePath={`/patients/${patientId}/timeline`} documentsPath={`/patients/${patientId}/docs`} />
      {chart.data && <HistorySections {...history} />}
      {!referrals.hidden && !referrals.loading && !referrals.failed && <OpenReferrals open={referrals.open} />}
    </div>
  );
}
