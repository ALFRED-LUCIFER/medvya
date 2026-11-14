import { Footprints, ReceiptText, UserPlus } from 'lucide-react';
import { Link, useNavigate } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { buttonVariants } from '@medvya/ui';
import StartConsult from '../features/encounters/components/StartConsult';
import { canConsult } from '../features/encounters/domain/encounter';
import { useStartConsultController } from '../features/encounters/hooks/useStartConsultController';
import type { BoardCard, VisitSteps } from '../features/front-desk/domain/board';
import BoardPage from '../features/front-desk/components/BoardPage';
import FrontDeskViews from '../features/front-desk/components/FrontDeskViews';
import { useBoardController } from '../features/front-desk/hooks/useBoardController';
import { FollowUpDismissDialog, FollowUpDueLine } from '../features/front-desk/components/FollowUpDue';
import { bookFollowUpPath } from '../features/front-desk/domain/followUpDue';
import { useFollowUpDueController } from '../features/front-desk/hooks/useFollowUpDueController';

type Props = {
  facilityId: string; online: boolean; canMove: boolean;
  /** May cancel a visit from the board's card menu: full "Register / book patients" (the server checks again). */
  canCancel: boolean;
  /** Register patient, when this role may register (from the navigation). */
  registerPath: string | null;
  /** Walk-in (MDV-P1-025), when this role may book and check in. */
  walkInPath: string | null;
  /** The waitlist (MDV-P1-027), when this role may book. */
  waitlistPath: string | null;
  /** The schedule, when this role may book: a follow-up's Book opens it (MDV-P1-102). */
  bookPath: string | null;
  /** Billing, when the billing.invoices flag is on and this role may take payments (from the navigation). */
  billingPath: string | null;
  /** Doctors (Patient 360 at Full) start consultations from the board (MDV-P2-001); the server checks clinical access each time. */
  canConsult: boolean;
  /** Where the nurse's intake of a visit opens, when the tenant has the nurse-intake step on and this role may work it (MDV-P2-007). */
  intakePath: ((appointmentId: string) => string) | null;
  /** The optional visit steps on for the tenant. With the nurse step on, a With nurse card is handed off from the intake, whoever looks at the board. */
  steps: VisitSteps;
};

export function Consult({ card, online, primary = true }: { card: BoardCard; online: boolean; primary?: boolean }) {
  const navigate = useNavigate();
  const consult = useStartConsultController(card.id, card.patientId, id => void navigate(`/consult/${id}`));
  return <StartConsult {...consult} patientName={card.patientName} inConsult={card.state === 'InConsult'} online={online} primary={primary} />;
}

/** Composition only: the board controller for the active facility, fed to the presentational P-01 page. */
export default function FrontDeskRoute({ facilityId, online, canMove, canCancel, registerPath, walkInPath, waitlistPath, billingPath, bookPath, canConsult: doctor, intakePath, steps }: Props) {
  const { t } = useI18n();
  const navigate = useNavigate();
  const board = useBoardController(facilityId, online);
  const dismissal = useFollowUpDueController();
  // One filled button per view: the desk's is Walk-in (or Register); a doctor's is the next patient's Start consultation.
  const register = registerPath || walkInPath ? <>
    {walkInPath && <Link to={walkInPath} className={buttonVariants({ variant: doctor ? 'outline' : 'default' })}><Footprints aria-hidden />{t('walkIn.action')}</Link>}
    {/* Words only beside Walk-in: the board's header is one row at 1024 px (MDV-P1-095). */}
    {registerPath && <Link to={registerPath} className={buttonVariants({ variant: walkInPath || doctor ? 'outline' : 'default' })}>{!walkInPath && <UserPlus aria-hidden />}{t('nav.register')}</Link>}
  </> : undefined;
  const payment = billingPath
    ? <Link to={billingPath} className={buttonVariants({ variant: 'outline', className: 'whitespace-normal' })}><ReceiptText aria-hidden />{t('board.action.takePayment')}</Link>
    : undefined;
  const waitlist = waitlistPath ? <Link to={waitlistPath} className={buttonVariants({ variant: 'outline' })}>{t('waitlist.title')}</Link> : undefined;
  const consultAction = doctor ? (card: BoardCard, primary: boolean) => (canConsult(card.state) ? <Consult key={card.id} card={card} online={online} primary={primary} /> : null) : undefined;
  const nurseStep = steps.nurse ? (card: BoardCard) => (intakePath
    ? <Link to={intakePath(card.id)} className={buttonVariants({ variant: 'outline', className: 'mt-1 min-h-11 whitespace-normal' })}
        aria-label={t('intake.queue.openFor', { name: card.patientName || t('board.unknownPatient') })}>{t('intake.queue.open')}</Link>
    : null) : undefined;
  // MDV-P1-102: the doctor's follow-up on the Done card; booking and dismissing need booking access (the server checks again).
  const followUp = (card: BoardCard) => card.followUp && <FollowUpDueLine followUp={card.followUp} patientName={card.patientName || t('board.unknownPatient')} online={online}
    bookPath={bookPath && bookFollowUpPath(bookPath, card.patientId, card.followUp)} onDismiss={bookPath ? dismissal.onStart : null} />;
  return <BoardPage {...board} steps={steps} canMove={canMove} canCancel={canCancel} registerAction={register} paymentAction={payment} overviewAction={waitlist} views={<FrontDeskViews value="board" onValueChange={v => { if (v === 'doctors') void navigate('/front-desk/overview'); }} />} consultAction={consultAction} nurseStep={nurseStep}
    followUp={followUp} followUpDialog={<FollowUpDismissDialog {...dismissal} online={online} />} />;
}
