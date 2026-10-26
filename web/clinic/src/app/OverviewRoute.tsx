import { LayoutGrid } from 'lucide-react';
import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { buttonVariants } from '@medvya/ui';
import ClinicOverviewPage from '../features/front-desk/components/ClinicOverviewPage';
import { useOverviewController } from '../features/front-desk/hooks/useOverviewController';

type Props = { facilityId: string; online: boolean; canMove: boolean; userId: string; boardPath: string };

/** Composition only: the D-04 clinic overview of the active facility (MDV-P1-030). */
export default function OverviewRoute({ facilityId, online, canMove, userId, boardPath }: Props) {
  const { t } = useI18n();
  const board = <Link to={boardPath} className={buttonVariants({ variant: 'outline' })}><LayoutGrid aria-hidden />{t('overview.toBoard')}</Link>;
  return <ClinicOverviewPage {...useOverviewController(facilityId, online, userId)} canMove={canMove} boardAction={board} />;
}
