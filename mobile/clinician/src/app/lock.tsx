import { LockScreen } from '../features/session/components/LockScreen';
import { useLockController } from '../features/session/hooks/useLockController';

export default function LockRoute() {
  return <LockScreen {...useLockController()} />;
}
