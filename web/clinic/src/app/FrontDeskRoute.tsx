import { UserPlus } from 'lucide-react';
import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { buttonVariants } from '@medvya/ui';
import BoardPage from '../features/front-desk/components/BoardPage';
import { useBoardController } from '../features/front-desk/hooks/useBoardController';

type Props = { facilityId: string; online: boolean; canMove: boolean; canRegister: boolean };

/** Composition only: the board controller for the active facility, fed to the presentational P-01 page. */
export default function FrontDeskRoute({ facilityId, online, canMove, canRegister }: Props) {
  const { t } = useI18n();
  const board = useBoardController(facilityId, online);
  const register = canRegister
    ? <Link to="/patients/new" className={buttonVariants()}><UserPlus aria-hidden />{t('nav.register')}</Link>
    : undefined;
  return <BoardPage {...board} canMove={canMove} registerAction={register} />;
}
