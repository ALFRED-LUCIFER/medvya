import { Footprints, ListChecks, ReceiptText, Stethoscope, UserPlus } from 'lucide-react';
import { Link, useNavigate } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { buttonVariants } from '@medvya/ui';
import StartConsult from '../features/encounters/components/StartConsult';
import { canConsult } from '../features/encounters/domain/encounter';
import { useStartConsultController } from '../features/encounters/hooks/useStartConsultController';
import type { BoardCard } from '../features/front-desk/domain/board';
import BoardPage from '../features/front-desk/components/BoardPage';
import { useBoardController } from '../features/front-desk/hooks/useBoardController';

type Props = {
  facilityId: string; online: boolean; canMove: boolean;
  /** Register patient, when this role may register (from the navigation). */
  registerPath: string | null;
  /** Walk-in (MDV-P1-025), when this role may book and check in. */
  walkInPath: string | null;
  /** The waitlist (MDV-P1-027), when this role may book. */
  waitlistPath: string | null;
  /** Billing, when the billing.invoices flag is on and this role may take payments (from the navigation). */
  billingPath: string | null;
  /** Doctors (Patient 360 at Full) start consultations from the board (MDV-P2-001); the server checks clinical access each time. */
  canConsult: boolean;
  /** Where the nurse's intake of a visit opens, when the tenant has the nurse-intake step on and this role may work it (MDV-P2-007). */
  intakePath: ((appointmentId: string) => string) | null;
  /** The nurse-intake step is on: a With nurse card is handed off from the intake, whoever looks at the board. */
  nurseStepOn: boolean;
};

export function Consult({ card, online }: { card: BoardCard; online: boolean }) {
  const navigate = useNavigate();
  const consult = useStartConsultController(card.id, card.patientId, id => void navigate(`/consult/${id}`));
  return <StartConsult {...consult} patientName={card.patientName} inConsult={card.state === 'InConsult'} online={online} />;
}

/** Composition only: the board controller for the active facility, fed to the presentational P-01 page. */
export default function FrontDeskRoute({ facilityId, online, canMove, registerPath, walkInPath, waitlistPath, billingPath, canConsult: doctor, intakePath, nurseStepOn }: Props) {
  const { t } = useI18n();
  const board = useBoardController(facilityId, online);
  const register = registerPath || walkInPath ? <>
    {walkInPath && <Link to={walkInPath} className={buttonVariants()}><Footprints aria-hidden />{t('walkIn.action')}</Link>}
    {registerPath && <Link to={registerPath} className={buttonVariants({ variant: walkInPath ? 'outline' : 'default' })}><UserPlus aria-hidden />{t('nav.register')}</Link>}
  </> : undefined;
  const payment = billingPath
    ? <Link to={billingPath} className={buttonVariants({ variant: 'outline', className: 'whitespace-normal' })}><ReceiptText aria-hidden />{t('board.action.takePayment')}</Link>
    : undefined;
  const overview = <Link to="/front-desk/overview" className={buttonVariants({ variant: 'outline' })}><Stethoscope aria-hidden />{t('overview.title')}</Link>;
  const waitlist = waitlistPath ? <Link to={waitlistPath} className={buttonVariants({ variant: 'outline' })}><ListChecks aria-hidden />{t('waitlist.title')}</Link> : undefined;
  const consultAction = doctor ? (card: BoardCard) => (canConsult(card.state) ? <Consult key={card.id} card={card} online={online} /> : null) : undefined;
  const nurseStep = nurseStepOn ? (card: BoardCard) => (intakePath
    ? <Link to={intakePath(card.id)} className={buttonVariants({ variant: 'outline', className: 'mt-1 min-h-11 whitespace-normal' })}
        aria-label={t('intake.queue.openFor', { name: card.patientName || t('board.unknownPatient') })}>{t('intake.queue.open')}</Link>
    : null) : undefined;
  return <BoardPage {...board} canMove={canMove} registerAction={register} paymentAction={payment} overviewAction={<>{overview}{waitlist}</>} consultAction={consultAction} nurseStep={nurseStep} />;
}
