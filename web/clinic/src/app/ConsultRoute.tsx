import { useParams } from 'react-router';
import ConsultPage from '../features/encounters/components/ConsultPage';
import { useConsultController } from '../features/encounters/hooks/useConsultController';

/** Composition only: one consultation (MDV-P2-001) by its id in the address; the server checks clinical access on every call. */
export default function ConsultRoute({ boardPath, canReconcile }: { boardPath: string; canReconcile: boolean }) {
  const { encounterId = '' } = useParams();
  return <Consult key={encounterId} encounterId={encounterId} boardPath={boardPath} canReconcile={canReconcile} />;
}

function Consult({ encounterId, boardPath, canReconcile }: { encounterId: string; boardPath: string; canReconcile: boolean }) {
  return <ConsultPage {...useConsultController(encounterId)} boardPath={boardPath} canReconcile={canReconcile} />;
}
