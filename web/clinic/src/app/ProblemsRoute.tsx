import { useParams } from 'react-router';
import ProblemsPage from '../features/problems/components/ProblemsPage';
import { useProblemsController } from '../features/problems/hooks/useProblemsController';

/** Composition only: one consultation's problem list and diagnosis coding (MDV-P2-010, E-07) by its id in the address; the server checks clinical access on every call. */
export default function ProblemsRoute({ boardPath, online }: { boardPath: string; online: boolean }) {
  const { encounterId = '' } = useParams();
  return <Problems key={encounterId} encounterId={encounterId} boardPath={boardPath} online={online} />;
}

function Problems({ encounterId, boardPath, online }: { encounterId: string; boardPath: string; online: boolean }) {
  return <ProblemsPage {...useProblemsController(encounterId)} online={online} consultPath={`/consult/${encounterId}`} boardPath={boardPath} />;
}
