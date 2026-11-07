import { useParams } from 'react-router';
import ConsultPage from '../features/encounters/components/ConsultPage';
import { useConsultController } from '../features/encounters/hooks/useConsultController';

/** Composition only: one consultation (MDV-P2-001) by its id in the address; the server checks clinical access on every call. */
export default function ConsultRoute({ boardPath }: { boardPath: string }) {
  const { encounterId = '' } = useParams();
  return <Consult key={encounterId} encounterId={encounterId} boardPath={boardPath} />;
}

function Consult({ encounterId, boardPath }: { encounterId: string; boardPath: string }) {
  return <ConsultPage {...useConsultController(encounterId)} boardPath={boardPath} />;
}
