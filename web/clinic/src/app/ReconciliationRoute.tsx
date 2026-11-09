import { useParams } from 'react-router';
import ReconciliationPage from '../features/reconciliation/components/ReconciliationPage';
import { useReconciliationController } from '../features/reconciliation/hooks/useReconciliationController';

/** Composition only: one consultation's medication reconciliation (MDV-P2-011, E-07) by its id in the address; the server checks clinical access on every call. */
export default function ReconciliationRoute({ boardPath, online }: { boardPath: string; online: boolean }) {
  const { encounterId = '' } = useParams();
  return <Reconcile key={encounterId} encounterId={encounterId} boardPath={boardPath} online={online} />;
}

function Reconcile({ encounterId, boardPath, online }: { encounterId: string; boardPath: string; online: boolean }) {
  return <ReconciliationPage {...useReconciliationController(encounterId)} online={online} consultPath={`/consult/${encounterId}`} boardPath={boardPath} />;
}
