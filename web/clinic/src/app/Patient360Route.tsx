import { useParams } from 'react-router';
import ChildHealthCard from '../features/child-health/components/ChildHealthCard';
import { useChildHealthController } from '../features/child-health/hooks/useChildHealthController';
import OpenReferrals from '../features/referrals/components/OpenReferrals';
import { useOpenReferralsController } from '../features/referrals/hooks/useReferralsController';
import HistorySections from '../features/patient360/components/HistorySections';
import PregnancyCard from '../features/patient360/components/PregnancyCard';
import Patient360Page from '../features/patient360/components/Patient360Page';
import { useHistoryController } from '../features/patient360/hooks/useHistoryController';
import AddMedicinePanel from '../features/prescription/components/AddMedicinePanel';
import { useAddMedicineController } from '../features/prescription/hooks/useAddMedicineController';
import { usePatient360Controller } from '../features/patient360/hooks/usePatient360Controller';
import { usePregnancyController } from '../features/patient360/hooks/usePregnancyController';

type Props = { backPath: string; canEditHistory: boolean; canManagePregnancy: boolean; canOrder: boolean; online: boolean };

/** Composition only: one patient's chart (MDV-P2-004) by id in the address; the server checks clinical access and audits every open. */
export default function Patient360Route(props: Props) {
  const { patientId = '' } = useParams();
  return <Chart key={patientId} patientId={patientId} {...props} />;
}

function Chart({ patientId, backPath, canEditHistory, canManagePregnancy, canOrder, online }: Props & { patientId: string }) {
  const referrals = useOpenReferralsController(patientId);
  const chart = usePatient360Controller(patientId);
  const history = useHistoryController(patientId, chart.data, canEditHistory);
  // Add medicine (MDV-P2-039) goes on the open consultation's draft; with none open the panel says to start one. The server still decides.
  const add = useAddMedicineController(chart.data?.chart.openEncounterId ?? null);
  const d = chart.data;
  // Immunisations and milestones (MDV-P2-036): asked for only once the chart says the patient is a child. Recording needs the same
  // capability as the history sections (vitals and allergies entry at Full: doctor or nurse).
  const child = d?.lifeStage === 'Child';
  const childHealth = useChildHealthController(patientId, child);
  const pregnancy = usePregnancyController(patientId, d, canManagePregnancy, canEditHistory);
  return (
    <div className="flex flex-col gap-4">
      <Patient360Page {...chart} backPath={backPath}
        addMedicine={canOrder && d && <AddMedicinePanel {...add} patient={{ ...d.patient, allergies: { state: d.chart.allergyState, allergies: d.chart.allergies } }} />}
        consultPath={id => `/consult/${id}`} timelinePath={`/patients/${patientId}/timeline`} documentsPath={`/patients/${patientId}/docs`} />
      {pregnancy.shown && <PregnancyCard {...pregnancy} />}
      {chart.data && <HistorySections {...history} />}
      {child && <ChildHealthCard {...childHealth} canRecord={canEditHistory} online={online} />}
      {!referrals.hidden && !referrals.loading && !referrals.failed && <OpenReferrals open={referrals.open} />}
    </div>
  );
}
