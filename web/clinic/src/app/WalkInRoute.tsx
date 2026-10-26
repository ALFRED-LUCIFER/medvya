import { useSearchParams } from 'react-router';
import WalkInPage from '../features/front-desk/components/WalkInPage';
import { useWalkInController } from '../features/front-desk/hooks/useWalkInController';

type Props = { facilityId: string; boardPath: string; registerPath: string };

/** Composition only: ?patient= (an opaque id, set by quick registration's "Check in now") preselects the patient. */
export default function WalkInRoute({ facilityId, boardPath, registerPath }: Props) {
  const [params] = useSearchParams();
  return <WalkInPage {...useWalkInController(facilityId, params.get('patient'))} boardPath={boardPath} registerPath={registerPath} />;
}
