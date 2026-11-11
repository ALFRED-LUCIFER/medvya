import { useParams } from 'react-router';
import ConsultPage from '../features/encounters/components/ConsultPage';
import NoteEditor from '../features/encounters/components/NoteEditor';
import NoteHistory from '../features/encounters/components/NoteHistory';
import NotePrintPage from '../features/encounters/components/NotePrintPage';
import NoteTemplateChoice from '../features/encounters/components/NoteTemplateChoice';
import { useConsultController } from '../features/encounters/hooks/useConsultController';
import OrdersPanel from '../features/orders/components/OrdersPanel';
import PrescriptionPanel from '../features/prescription/components/PrescriptionPanel';
import RxPrintPage from '../features/prescription/components/RxPrintPage';
import RxSharePanel from '../features/prescription/components/RxSharePanel';
import { usePrescriptionController } from '../features/prescription/hooks/usePrescriptionController';
import { useRxPrintController } from '../features/prescription/hooks/useRxPrintController';
import { useRxShareController } from '../features/prescription/hooks/useRxShareController';
import LetterPage from '../features/referrals/components/LetterPage';
import ReferralsPanel from '../features/referrals/components/ReferralsPanel';
import { useLetterController, useReferralsController } from '../features/referrals/hooks/useReferralsController';
import SlipPage from '../features/orders/components/SlipPage';
import { useOrdersController, useSlipController } from '../features/orders/hooks/useOrdersController';
import { useNoteController } from '../features/encounters/hooks/useNoteController';
import { useNoteTemplatesController } from '../features/encounters/hooks/useNoteTemplatesController';
import { useAddendumController, useNotePrintController } from '../features/encounters/hooks/useSignedNoteController';

/** Composition only: one consultation (MDV-P2-001) by its id in the address, with its note (MDV-P2-009); the server checks clinical access on every call. */
type Props = { boardPath: string; canOrder: boolean; canReconcile: boolean; canSign: boolean };

export default function ConsultRoute({ boardPath, ...rest }: Props) {
  const { encounterId = '' } = useParams();
  return <Consult key={encounterId} encounterId={encounterId} boardPath={boardPath} {...rest} />;
}

function Consult({ encounterId, boardPath, canOrder, canReconcile, canSign }: Props & { encounterId: string }) {
  const consult = useConsultController(encounterId);
  return (
    <div className="flex flex-col gap-4">
      <ConsultPage {...consult} boardPath={boardPath} canReconcile={canReconcile}>
        <NoteEditor {...useNoteController(encounterId, canSign, consult.banner?.timeZone)} timeZone={consult.banner?.timeZone}
          patient={consult.banner && { name: consult.banner.patient.fullName, mrn: consult.banner.patient.mrn, dateOfBirth: consult.banner.patient.dateOfBirth, encounterId }} />
        <NoteHistory {...useAddendumController(encounterId, canSign)} timeZone={consult.banner?.timeZone} printPath={`/consult/${encounterId}/note/print`} />
        <NoteTemplateChoice {...useNoteTemplatesController()} />
      </ConsultPage>
      {consult.banner && canOrder && <Prescription encounterId={encounterId} ended={consult.banner.encounter.status === 'Ended'} />}
      {consult.banner && canOrder && <Orders encounterId={encounterId} ended={consult.banner.encounter.status === 'Ended'} />}
      {consult.banner && canOrder && <Referrals patientId={consult.banner.encounter.patientId} encounterId={encounterId} ended={consult.banner.encounter.status === 'Ended'} />}
    </div>
  );
}

/** The draft prescription (MDV-P2-015) under the consultation, for a role with "Prescriptions and orders" at Sign; the server still decides for this clinician and patient. */
function Prescription({ encounterId, ended }: { encounterId: string; ended: boolean }) {
  const rx = usePrescriptionController(encounterId);
  return rx.hidden ? null : <PrescriptionPanel {...rx} ended={ended} printPath={`/consult/${encounterId}/rx/print`} />;
}

/** Orders (MDV-P2-018) under the consultation, for a role with "Prescriptions and orders" at Sign; the server still decides for this clinician and patient. */
function Orders({ encounterId, ended }: { encounterId: string; ended: boolean }) {
  const orders = useOrdersController(encounterId);
  return orders.hidden ? null : <OrdersPanel {...orders} encounterId={encounterId} ended={ended} />;
}

/** Referrals out and in (MDV-P2-019) under the consultation; a clinician the server does not let write sees nothing here. */
function Referrals({ patientId, encounterId, ended }: { patientId: string; encounterId: string; ended: boolean }) {
  const referrals = useReferralsController(patientId, encounterId);
  return referrals.hidden ? null : <ReferralsPanel {...referrals} encounterId={encounterId} ended={ended} />;
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
