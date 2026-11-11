import { useNavigate, useParams } from 'react-router';
import IntakePage from '../features/nurse-intake/components/IntakePage';
import NurseQueuePage from '../features/nurse-intake/components/NurseQueuePage';
import { useIntakeController } from '../features/nurse-intake/hooks/useIntakeController';
import { useBoardController } from '../features/front-desk/hooks/useBoardController';

/** Composition only: the nurse queue (MDV-P2-007) is the board's With nurse column, plus Checked in to take to intake (#956); opening a card opens its intake. */
export function NurseQueueRoute({ facilityId, online, nursePath }: { facilityId: string; online: boolean; nursePath: string }) {
  const board = useBoardController(facilityId, online);
  return <NurseQueuePage board={board.board} cards={board.columns.WithNurse} checkedIn={board.columns.CheckedIn} now={board.now} loading={board.loading}
    offline={board.offline} forbidden={board.forbidden} failed={board.failed} live={board.live} pendingId={board.pendingId} failure={board.failure}
    intakePath={id => `${nursePath}/${id}`} onRetry={board.onRetry} onTake={id => board.onMove(id, 'WithNurse')} />;
}

/** One visit's intake by its id in the address; the server checks clinical access on every call. After the handoff the nurse is back at the queue. */
export function NurseIntakeRoute({ online, nursePath }: { online: boolean; nursePath: string }) {
  const { appointmentId = '' } = useParams();
  return <Intake key={appointmentId} appointmentId={appointmentId} online={online} nursePath={nursePath} />;
}

function Intake({ appointmentId, online, nursePath }: { appointmentId: string; online: boolean; nursePath: string }) {
  const navigate = useNavigate();
  const intake = useIntakeController(appointmentId, () => void navigate(nursePath));
  return <IntakePage {...intake} online={online} queuePath={nursePath} />;
}
