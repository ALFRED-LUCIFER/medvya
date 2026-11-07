import AdjustmentsPage from '../features/schedule/components/AdjustmentsPage';
import { useAdjustmentController } from '../features/schedule/hooks/useAdjustmentController';

type Props = { facilityId: string; canConfigure: boolean; userId: string; schedulePath: string; boardPath: string | null };

/** Composition only: leave and schedule exceptions (MDV-P1-012) for the active facility. */
export default function AdjustmentsRoute({ facilityId, canConfigure, userId, schedulePath, boardPath }: Props) {
  return <AdjustmentsPage {...useAdjustmentController(facilityId, canConfigure, userId)} backPath={schedulePath} boardPath={boardPath} />;
}
