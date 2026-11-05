import AdjustmentsPage from '../features/schedule/components/AdjustmentsPage';
import { useAdjustmentController } from '../features/schedule/hooks/useAdjustmentController';

type Props = { facilityId: string; schedulePath: string; boardPath: string | null };

/** Composition only: leave and schedule exceptions (MDV-P1-012) for the active facility. */
export default function AdjustmentsRoute({ facilityId, schedulePath, boardPath }: Props) {
  return <AdjustmentsPage {...useAdjustmentController(facilityId)} backPath={schedulePath} boardPath={boardPath} />;
}
