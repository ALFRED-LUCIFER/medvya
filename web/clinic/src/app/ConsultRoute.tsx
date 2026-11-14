import { useParams } from 'react-router';
import ConsultPage, { EndConsultation } from '../features/encounters/components/ConsultPage';
import ConsultStudio, { ConsultFooter } from '../features/encounters/components/ConsultStudio';
import Disclosure from '../features/encounters/components/Disclosure';
import NoteEditor from '../features/encounters/components/NoteEditor';
import NoteHistory from '../features/encounters/components/NoteHistory';
import NotePrintPage from '../features/encounters/components/NotePrintPage';
import NoteTemplateChoice from '../features/encounters/components/NoteTemplateChoice';
import { useConsultController } from '../features/encounters/hooks/useConsultController';
import FollowUpChoice from '../features/encounters/components/FollowUpChoice';
import { useFollowUpController } from '../features/encounters/hooks/useFollowUpController';
import OrdersPanel from '../features/orders/components/OrdersPanel';
import TestToggles from '../features/orders/components/TestToggles';
import AdviceField from '../features/prescription/components/AdviceField';
import { useAdviceController } from '../features/prescription/hooks/useAdviceController';
import MedicineActions from '../features/reconciliation/components/MedicineActions';
import { useReconciliationController } from '../features/reconciliation/hooks/useReconciliationController';
import ReferralJump from '../features/referrals/components/ReferralJump';
import AddMedicinePanel, { type AddMedicinePatient } from '../features/prescription/components/AddMedicinePanel';
import PrescriptionPanel from '../features/prescription/components/PrescriptionPanel';
import RxPrintPage from '../features/prescription/components/RxPrintPage';
import RxSharePanel from '../features/prescription/components/RxSharePanel';
import { useAddMedicineController } from '../features/prescription/hooks/useAddMedicineController';
import { usePrescriptionController } from '../features/prescription/hooks/usePrescriptionController';
import { useRxPrintController } from '../features/prescription/hooks/useRxPrintController';
import { useRxShareController } from '../features/prescription/hooks/useRxShareController';
import LetterPage from '../features/referrals/components/LetterPage';
import ReferralsPanel from '../features/referrals/components/ReferralsPanel';
import { useLetterController, useReferralsController } from '../features/referrals/hooks/useReferralsController';
import SlipPage from '../features/orders/components/SlipPage';
import { useOrdersController, useSlipController } from '../features/orders/hooks/useOrdersController';
import { useNoteController } from '../features/encounters/hooks/useNoteController';
import ConsultEntry from '../features/nurse-intake/components/ConsultEntry';
import { useIntakeController } from '../features/nurse-intake/hooks/useIntakeController';
import { useNoteTemplatesController } from '../features/encounters/hooks/useNoteTemplatesController';
import { useAddendumController, useNotePrintController } from '../features/encounters/hooks/useSignedNoteController';

/** Composition only: one consultation (MDV-P2-001) by its id in the address, with its note (MDV-P2-009); the server checks clinical access on every call. */
type Props = { boardPath: string; online: boolean; canOrder: boolean; canReconcile: boolean; canSign: boolean; userId: string };

export default function ConsultRoute({ boardPath, ...rest }: Props) {
  const { encounterId = '' } = useParams();
  return <Consult key={encounterId} encounterId={encounterId} boardPath={boardPath} {...rest} />;
}

function Consult({ encounterId, boardPath, online, canOrder, canReconcile, canSign, userId }: Props & { encounterId: string }) {
  const consult = useConsultController(encounterId);
  // One controller for both views (MDV-P2-041): switching keeps the form, the autosave and every typed word.
  const note = { ...useNoteController(encounterId, canSign, consult.banner?.timeZone, consult.banner), timeZone: consult.banner?.timeZone,
    patient: consult.banner && { name: consult.banner.patient.fullName, mrn: consult.banner.patient.mrn, dateOfBirth: consult.banner.patient.dateOfBirth } };
  const ended = consult.banner?.encounter.status === 'Ended';
  const patient = consult.banner && { ...consult.banner.patient, allergies: consult.banner.allergies };
  const plan = patient && <Plan encounterId={encounterId} ended={ended} online={online} canOrder={canOrder} canReconcile={canReconcile} patient={patient} />;
  // Vitals, weight and allergies by the doctor (MDV audit P0-2): in the one page's Vitals section, above the note in the guided steps.
  const entry = (titled: boolean) => consult.banner && !ended
    && <Entry appointmentId={consult.banner.encounter.appointmentId} online={online} onChanged={consult.onChartChanged} titled={titled} />;
  // Everything of the visit sits inside ConsultPage, so its pinned patient strip and footer stay for the whole page (MDV-P1-092).
  return (
    <ConsultPage {...consult} boardPath={boardPath} canReconcile={canReconcile}
      canConfirmIdentity={canSign && consult.banner?.encounter.leadClinicianId === userId}
      footer={consult.view === 'page' && <ConsultFooter {...note} finish={!ended && <EndConsultation ending={consult.ending} onEnd={consult.onEnd} />} />}>
      {consult.view === 'page' ? <ConsultStudio {...note} panel={consult.panel} onPanel={consult.onPanel} chart={consult.chart} plan={plan} entry={entry(false)} />
        : <>{entry(true)}<NoteEditor {...note} /></>}
      <NoteHistory {...useAddendumController(encounterId, canSign)} timeZone={consult.banner?.timeZone} printPath={`/consult/${encounterId}/note/print`} />
      <NoteTemplateChoice {...useNoteTemplatesController()} />
      {patient && canOrder && <Prescription encounterId={encounterId} ended={ended} patient={patient} />}
      {consult.banner && canOrder && <Orders encounterId={encounterId} ended={ended} />}
      {consult.banner && canOrder && <Referrals patientId={consult.banner.encounter.patientId} encounterId={encounterId} ended={ended} />}
    </ConsultPage>
  );
}

/**
 * The one page's Plan (MDV-P2-043): each chart medicine's action (the reconciliation), Add medicine into the prescription (whose composer stays
 * under the note), test toggles, advice for the patient, follow-up and the way to a referral. Each part asks the server, which decides.
 */
function Plan({ encounterId, ended, online, canOrder, canReconcile, patient }: {
  encounterId: string; ended: boolean; online: boolean; canOrder: boolean; canReconcile: boolean; patient: AddMedicinePatient;
}) {
  return (
    <div className="flex flex-col gap-5 border-t pt-4">
      {canReconcile && <Medicines encounterId={encounterId} online={online} />}
      {canOrder && !ended && <AddMedicine encounterId={encounterId} patient={patient} />}
      {canOrder && <Tests encounterId={encounterId} ended={ended} />}
      {canOrder && <Advice encounterId={encounterId} ended={ended} />}
      {canReconcile && <FollowUp encounterId={encounterId} ended={ended} />}
      {canOrder && <ReferralJump />}
    </div>
  );
}

function Entry({ appointmentId, online, onChanged, titled }: { appointmentId: string; online: boolean; onChanged: () => void; titled: boolean }) {
  return <ConsultEntry {...useIntakeController(appointmentId, () => {}, onChanged)} online={online} titled={titled} />;
}

function Medicines({ encounterId, online }: { encounterId: string; online: boolean }) {
  const rec = useReconciliationController(encounterId);
  return rec.refusal ? null : <MedicineActions {...rec} online={online} reconcilePath={`/consult/${encounterId}/medicines`} />;
}

function AddMedicine({ encounterId, patient }: { encounterId: string; patient: AddMedicinePatient }) {
  const add = useAddMedicineController(encounterId);
  return <AddMedicinePanel {...add} patient={patient} />;
}

function Tests({ encounterId, ended }: { encounterId: string; ended: boolean }) {
  const orders = useOrdersController(encounterId);
  return orders.hidden ? null : <TestToggles {...orders} ended={ended} />;
}

function Advice({ encounterId, ended }: { encounterId: string; ended: boolean }) {
  const advice = useAdviceController(encounterId);
  return advice.hidden ? null : <AdviceField {...advice} ended={ended} />;
}

function FollowUp({ encounterId, ended }: { encounterId: string; ended: boolean }) {
  const followUp = useFollowUpController(encounterId);
  return followUp.hidden ? null : <FollowUpChoice {...followUp} ended={ended} />;
}

/** The draft prescription (MDV-P2-015) under the consultation, for a role with "Prescriptions and orders" at Sign; the server still decides for this clinician and patient. */
function Prescription({ encounterId, ended, patient }: { encounterId: string; ended: boolean; patient: AddMedicinePatient }) {
  const rx = usePrescriptionController(encounterId);
  const add = useAddMedicineController(encounterId);
  return rx.hidden ? null : <PrescriptionPanel {...rx} ended={ended} printPath={`/consult/${encounterId}/rx/print`} addMedicine={<AddMedicinePanel {...add} patient={patient} />} />;
}

/** Orders (MDV-P2-018) under the consultation, for a role with "Prescriptions and orders" at Sign; the server still decides for this clinician and patient. */
function Orders({ encounterId, ended }: { encounterId: string; ended: boolean }) {
  const orders = useOrdersController(encounterId);
  return orders.hidden ? null : <Disclosure titleKey="orders.title" filled={orders.orders.length > 0}><OrdersPanel {...orders} encounterId={encounterId} ended={ended} /></Disclosure>;
}

/** Referrals out and in (MDV-P2-019) under the consultation; a clinician the server does not let write sees nothing here. */
function Referrals({ patientId, encounterId, ended }: { patientId: string; encounterId: string; ended: boolean }) {
  const referrals = useReferralsController(patientId, encounterId);
  return referrals.hidden ? null : <Disclosure titleKey="referrals.title" filled={referrals.referrals.length > 0}><ReferralsPanel {...referrals} encounterId={encounterId} ended={ended} /></Disclosure>;
}

/** The referral letter (MDV-P2-019), printable. */
export function LetterRoute() {
  const { encounterId = '', referralId = '' } = useParams();
  return <LetterPage {...useLetterController(referralId)} consultPath={`/consult/${encounterId}`} />;
}

/** The investigation slip of one consultation (MDV-P2-018), printable. */
export function SlipRoute() {
  const { encounterId = '' } = useParams();
  return <SlipPage {...useSlipController(encounterId)} consultPath={`/consult/${encounterId}`} />;
}

/** The signed note's print view and PDF (MDV-P2-013), with the patient's identifiers from the consultation. */
export function NotePrintRoute() {
  const { encounterId = '' } = useParams();
  const print = useNotePrintController(encounterId);
  const consult = useConsultController(encounterId);
  return <NotePrintPage {...print} loading={print.loading || consult.loading} failed={print.failed || consult.failed} banner={consult.banner}
    consultPath={`/consult/${encounterId}`} />;
}

/** The printed prescription of one consultation (MDV-P2-017): only once authorised; the server decides for this clinician and patient. */
export function RxPrintRoute() {
  const { encounterId = '' } = useParams();
  const print = useRxPrintController(encounterId);
  const share = useRxShareController(encounterId);
  return <RxPrintPage {...print} consultPath={`/consult/${encounterId}`} share={print.print && <RxSharePanel {...share} timeZone={print.print.timeZone} />} />;
}
