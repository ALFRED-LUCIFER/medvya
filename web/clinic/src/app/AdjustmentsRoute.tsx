import { useI18n } from '@medvya/i18n';
import TabletOnly from './TabletOnly';
import AdjustmentsPage from '../features/schedule/components/AdjustmentsPage';
import { useAdjustmentController } from '../features/schedule/hooks/useAdjustmentController';

type Props = { facilityId: string; canConfigure: boolean; userId: string; schedulePath: string; boardPath: string | null };

/** Composition only: leave and schedule exceptions (MDV-P1-012) for the active facility. */
export default function AdjustmentsRoute({ facilityId, canConfigure, userId, schedulePath, boardPath }: Props) {
  const { t } = useI18n();
  const adjustments = useAdjustmentController(facilityId, canConfigure, userId);
  // Schedule editing waits for a tablet or computer (MDV-P1-098).
  return <TabletOnly title={t('adjustment.title')}><AdjustmentsPage {...adjustments} backPath={schedulePath} boardPath={boardPath} /></TabletOnly>;
}
