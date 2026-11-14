import { useParams, useSearchParams } from 'react-router';
import TokenSlipPrintPage from '../features/front-desk/components/TokenSlipPrintPage';
import WalkInPage from '../features/front-desk/components/WalkInPage';
import { useTokenSlipController } from '../features/front-desk/hooks/useTokenSlipController';
import { useWalkInController } from '../features/front-desk/hooks/useWalkInController';

type Props = { facilityId: string; boardPath: string; registerPath: string };

/** Composition only: ?patient= (an opaque id, set by quick registration's "Check in now") preselects the patient. */
export default function WalkInRoute({ facilityId, boardPath, registerPath }: Props) {
  const [params] = useSearchParams();
  return <WalkInPage {...useWalkInController(facilityId, params.get('patient'))} boardPath={boardPath} registerPath={registerPath} />;
}

/** The token slip of one visit (MDV-P2-046), from Walk-in's result or a waiting card's Reprint; the server decides. */
export function TokenSlipRoute({ boardPath }: { boardPath: string }) {
  const { appointmentId = '' } = useParams();
  return <TokenSlipPrintPage {...useTokenSlipController(appointmentId)} boardPath={boardPath} />;
}
