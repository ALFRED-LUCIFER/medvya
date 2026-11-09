import { useParams } from 'react-router';
import ConsultPage from '../features/encounters/components/ConsultPage';
import { useConsultController } from '../features/encounters/hooks/useConsultController';
import OrdersPanel from '../features/orders/components/OrdersPanel';
import LetterPage from '../features/referrals/components/LetterPage';
import ReferralsPanel from '../features/referrals/components/ReferralsPanel';
import { useLetterController, useReferralsController } from '../features/referrals/hooks/useReferralsController';
import SlipPage from '../features/orders/components/SlipPage';
import { useOrdersController, useSlipController } from '../features/orders/hooks/useOrdersController';

/** Composition only: one consultation (MDV-P2-001) by its id in the address; the server checks clinical access on every call. */
export default function ConsultRoute({ boardPath, canOrder, canReconcile }: { boardPath: string; canOrder: boolean; canReconcile: boolean }) {
  const { encounterId = '' } = useParams();
  return <Consult key={encounterId} encounterId={encounterId} boardPath={boardPath} canOrder={canOrder} canReconcile={canReconcile} />;
}

function Consult({ encounterId, boardPath, canOrder, canReconcile }: { encounterId: string; boardPath: string; canOrder: boolean; canReconcile: boolean }) {
  const consult = useConsultController(encounterId);
  return (
    <div className="flex flex-col gap-4">
      <ConsultPage {...consult} boardPath={boardPath} canReconcile={canReconcile} />
      {consult.banner && canOrder && <Orders encounterId={encounterId} ended={consult.banner.encounter.status === 'Ended'} />}
      {consult.banner && canOrder && <Referrals patientId={consult.banner.encounter.patientId} encounterId={encounterId} ended={consult.banner.encounter.status === 'Ended'} />}
    </div>
  );
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
