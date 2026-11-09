import { useParams } from 'react-router';
import ConsultPage from '../features/encounters/components/ConsultPage';
import { useConsultController } from '../features/encounters/hooks/useConsultController';
import OrdersPanel from '../features/orders/components/OrdersPanel';
import SlipPage from '../features/orders/components/SlipPage';
import { useOrdersController, useSlipController } from '../features/orders/hooks/useOrdersController';

/** Composition only: one consultation (MDV-P2-001) by its id in the address; the server checks clinical access on every call. */
export default function ConsultRoute({ boardPath, canOrder }: { boardPath: string; canOrder: boolean }) {
  const { encounterId = '' } = useParams();
  return <Consult key={encounterId} encounterId={encounterId} boardPath={boardPath} canOrder={canOrder} />;
}

function Consult({ encounterId, boardPath, canOrder }: { encounterId: string; boardPath: string; canOrder: boolean }) {
  const consult = useConsultController(encounterId);
  return (
    <div className="flex flex-col gap-4">
      <ConsultPage {...consult} boardPath={boardPath} />
      {consult.banner && canOrder && <Orders encounterId={encounterId} ended={consult.banner.encounter.status === 'Ended'} />}
    </div>
  );
}

/** Orders (MDV-P2-018) under the consultation, for a role with "Prescriptions and orders" at Sign; the server still decides for this clinician and patient. */
function Orders({ encounterId, ended }: { encounterId: string; ended: boolean }) {
  const orders = useOrdersController(encounterId);
  return orders.hidden ? null : <OrdersPanel {...orders} encounterId={encounterId} ended={ended} />;
}

/** The investigation slip of one consultation (MDV-P2-018), printable. */
export function SlipRoute() {
  const { encounterId = '' } = useParams();
  return <SlipPage {...useSlipController(encounterId)} consultPath={`/consult/${encounterId}`} />;
}
