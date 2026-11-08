import { VisitsScreen } from '../../features/visits/components/VisitsScreen';
import { useVisitsController } from '../../features/visits/hooks/useVisitsController';

export default function BookRoute() {
  return <VisitsScreen {...useVisitsController()} />;
}
