import { useI18n } from '@medvya/i18n';
import TabletOnly from './TabletOnly';
import { useNavigate, useParams } from 'react-router';
import IntakePage from '../features/nurse-intake/components/IntakePage';
import NurseQueuePage from '../features/nurse-intake/components/NurseQueuePage';
import { useIntakeController } from '../features/nurse-intake/hooks/useIntakeController';
import { useBoardController } from '../features/front-desk/hooks/useBoardController';
import { useQueueTableController } from '../features/front-desk/hooks/useQueueTableController';
import type { BoardCard } from '../features/front-desk/domain/board';

const row = (c: BoardCard) => ({ id: c.id, patientName: c.patientName, doctor: c.calendarName, state: c.state, start: c.start, since: c.since, token: c.token });

/** Composition only: the nurse queue (MDV-P2-007) is the board's With nurse column, plus Checked in to take to intake (#956); opening a card opens its intake. */
export function NurseQueueRoute({ facilityId, online, nursePath }: { facilityId: string; online: boolean; nursePath: string }) {
  const board = useBoardController(facilityId, online);
  const withNurse = useQueueTableController(board.columns.WithNurse.map(row));
  const checkedIn = useQueueTableController(board.columns.CheckedIn.map(row));
  return <NurseQueuePage board={board.board} withNurse={withNurse} checkedIn={checkedIn} now={board.now} loading={board.loading}
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
  const { t } = useI18n();
  // Nurse vitals entry waits for a tablet until the strip is pinned and one group shows per screen (MDV-P1-098).
  return (
    <TabletOnly title={t('nav.nurse')} patientName={intake.screen?.patient.fullName}>
      <IntakePage {...intake} online={online} queuePath={nursePath} />
    </TabletOnly>
  );
}
