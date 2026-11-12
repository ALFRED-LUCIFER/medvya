import type { ComponentType } from 'react';
import { useParams } from 'react-router';
import ChildHealthCard from '../features/child-health/components/ChildHealthCard';
import { useChildHealthController } from '../features/child-health/hooks/useChildHealthController';
import DocumentsSummary from '../features/documents/components/DocumentsSummary';
import { useDocumentListController } from '../features/documents/hooks/useDocumentListController';
import OpenReferrals from '../features/referrals/components/OpenReferrals';
import { useOpenReferralsController } from '../features/referrals/hooks/useReferralsController';
import HistorySections from '../features/patient360/components/HistorySections';
import OlderAdultCards from '../features/patient360/components/OlderAdultCards';
import Patient360Page from '../features/patient360/components/Patient360Page';
import PregnancyCard from '../features/patient360/components/PregnancyCard';
import type { LifeStage } from '../features/patient360/domain/chart';
import { useChartSectionController } from '../features/patient360/hooks/useChartSectionController';
import { useHistoryController } from '../features/patient360/hooks/useHistoryController';
import { useOlderAdultController } from '../features/patient360/hooks/useOlderAdultController';
import { usePatient360Controller } from '../features/patient360/hooks/usePatient360Controller';
import { usePregnancyController } from '../features/patient360/hooks/usePregnancyController';
import AddMedicinePanel from '../features/prescription/components/AddMedicinePanel';
import { useAddMedicineController } from '../features/prescription/hooks/useAddMedicineController';
import TimelinePreview from '../features/timeline/components/TimelinePreview';
import { useTimelinePreviewController } from '../features/timeline/hooks/useTimelinePreviewController';

type Props = { backPath: string; canEditHistory: boolean; canManagePregnancy: boolean; canOrder: boolean; online: boolean; userId: string };
/** What every life-stage module gets; recording needs vitals and allergies entry at Full (doctor or nurse), and the server still decides. */
type StageModuleProps = { patientId: string; canRecord: boolean; online: boolean; openConsultPath: string | null };

/**
 * The life-stage modules (MDV-P2-034 to P2-038), by the life stage the server works out. Each is a route-level component that takes
 * StageModuleProps and fetches its own data; add it to its stage here and the chart shows it in the life-stage slot. A stage with none shows the empty card.
 * The pregnancy card (MDV-P2-034) takes the chart answer and the doctor's rights, so Chart places it: in the slot at the Pregnancy stage, and
 * below the chart for a woman of 10 to 55 at any other stage, so a doctor can start a pregnancy or a clinician record "not pregnant today".
 */
const stageModules: Record<LifeStage, ComponentType<StageModuleProps>[]> = { Adult: [], Child: [ChildHealth], Pregnancy: [], OlderAdult: [OlderAdult] };

/** Immunisations and milestones (MDV-P2-036), asked for only once the chart says the patient is a child. */
function ChildHealth({ patientId, canRecord, online }: StageModuleProps) {
  return <ChildHealthCard {...useChildHealthController(patientId, true)} canRecord={canRecord} online={online} />;
}

/** The older adult review and medicine review (MDV-P2-037); "plan change" opens the open consultation when there is one. */
function OlderAdult({ patientId, openConsultPath }: StageModuleProps) {
  return <OlderAdultCards {...useOlderAdultController(patientId, true)} consultPath={openConsultPath} />;
}

/** Composition only: one patient's chart (MDV-P2-004, MDV-P2-033) by id in the address; the server checks clinical access and audits every open. */
export default function Patient360Route(props: Props) {
  const { patientId = '' } = useParams();
  return <Chart key={patientId} patientId={patientId} {...props} />;
}

function Chart({ patientId, backPath, canEditHistory, canManagePregnancy, canOrder, online, userId }: Props & { patientId: string }) {
  const referrals = useOpenReferralsController(patientId);
  const chart = usePatient360Controller(patientId);
  const history = useHistoryController(patientId, chart.data, canEditHistory);
  // Add medicine (MDV-P2-039) goes on the open consultation's draft; with none open the panel says to start one. The server still decides.
  const add = useAddMedicineController(chart.data?.chart.openEncounterId ?? null);
  const d = chart.data;
  const modules = d ? stageModules[d.lifeStage] : [];
  const pregnancy = usePregnancyController(patientId, d, canManagePregnancy, canEditHistory);
  const pregnancyCard = pregnancy.shown && <PregnancyCard {...pregnancy} />;
  const pregnant = d?.lifeStage === 'Pregnancy';
  const consultPath = (id: string) => `/consult/${id}`;
  const openConsultPath = d?.chart.openEncounterId ? consultPath(d.chart.openEncounterId) : null;
  // The timeline and documents are fetched only once the chart has opened, so a refused chart asks the server nothing more.
  return (
    <div className="flex flex-col gap-4">
      <Patient360Page {...chart} {...useChartSectionController(userId)} backPath={backPath} consultPath={consultPath}
        addMedicine={canOrder && d && <AddMedicinePanel {...add} patient={{ ...d.patient, allergies: { state: d.chart.allergyState, allergies: d.chart.allergies } }} />}
        history={chart.data && <HistorySections {...history} />}
        stageModule={modules.length > 0 || (pregnant && pregnancy.shown) ? <div className="flex flex-col gap-4">
          {pregnant && pregnancyCard}
          {modules.map((M, i) => <M key={i} patientId={patientId} canRecord={canEditHistory} online={online} openConsultPath={openConsultPath} />)}
        </div> : undefined}
        timeline={chart.data && <Timeline patientId={patientId} />}
        documents={chart.data && <Documents patientId={patientId} />} />
      {!pregnant && pregnancyCard}
      {!referrals.hidden && !referrals.loading && !referrals.failed && <OpenReferrals open={referrals.open} />}
    </div>
  );
}

const Timeline = ({ patientId }: { patientId: string }) => <TimelinePreview {...useTimelinePreviewController(patientId)} timelinePath={`/patients/${patientId}/timeline`} />;
const Documents = ({ patientId }: { patientId: string }) => <DocumentsSummary {...useDocumentListController(patientId)} documentsPath={`/patients/${patientId}/docs`} />;
