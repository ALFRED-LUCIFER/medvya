import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { buttonVariants, PageContainer, PageHeader, PageMessage } from '@medvya/ui';
import type { Board, LiveStatus, MoveError } from '../../front-desk/domain/board';
import type { QueueRow, QueueTableView } from '../../front-desk/domain/queueTable';
import QueueTable from '../../front-desk/components/QueueTable';

type Props = {
  board: Board | null; now: number; loading: boolean; offline: boolean; forbidden: boolean; failed: boolean; live: LiveStatus;
  /** The board's With nurse and Checked in visits, each with its own sort (useQueueTableController). */
  withNurse: QueueTableView; checkedIn: QueueTableView;
  pendingId: string | null; failure: { id: string; error: MoveError } | null;
  intakePath: (appointmentId: string) => string;
  onRetry: () => void;
  onTake: (appointmentId: string) => void;
};

/**
 * N-01 queue (MDV-P2-007): the board's "With nurse" visits, longest wait first, each opening its intake; below them the "Checked in" visits the
 * nurse may take to intake without waiting for reception (#956). Nothing clinical shows here.
 */
export default function NurseQueuePage(p: Props) {
  const { t, tCount } = useI18n();
  const name = (r: QueueRow) => r.patientName || t('board.unknownPatient');
  const total = p.withNurse.rows.length + p.checkedIn.rows.length;
  let body;
  if (p.forbidden) body = <PageMessage kind="denied" title={t('err.403')}>{t('board.forbidden.body')}</PageMessage>;
  else if (p.offline) body = <PageMessage kind="error" title={t('state.offline.title')}>{t('board.offline')}</PageMessage>;
  else if (p.failed) body = <PageMessage kind="error" title={t('board.error.title')} action={<button type="button" className={buttonVariants()} onClick={p.onRetry}>{t('schedule.retry')}</button>}>{t('board.error.body')}</PageMessage>;
  else if (p.loading || !p.board) body = <PageMessage kind="busy">{t('intake.queue.loading')}</PageMessage>;
  else if (total === 0) body = <PageMessage kind="empty" title={t('intake.queue.empty.title')}>{t('intake.queue.empty.body')}</PageMessage>;
  else body = (
    <>
      {p.withNurse.rows.length > 0 && (
        <Section id="nq-with" title={t('intake.queue.withNurse')}>
          <QueueTable {...p.withNurse} label={t('intake.queue.withNurse')} timeZone={p.board.timeZone} now={p.now} action={r => (
            <Link to={p.intakePath(r.id)} className={buttonVariants({ className: 'whitespace-normal' })} aria-label={t('intake.queue.openFor', { name: name(r) })}>
              {t('intake.queue.open')}
            </Link>
          )} />
        </Section>
      )}
      {p.checkedIn.rows.length > 0 && (
        <Section id="nq-in" title={t('intake.queue.checkedIn')}>
          <QueueTable {...p.checkedIn} label={t('intake.queue.checkedIn')} timeZone={p.board.timeZone} now={p.now}
            action={r => (
              <button type="button" disabled={p.pendingId === r.id} onClick={() => p.onTake(r.id)} className={buttonVariants({ variant: 'outline', className: 'whitespace-normal' })}
                aria-label={t('intake.queue.takeFor', { name: name(r) })}>
                {t('intake.queue.take')}
              </button>
            )}
            detail={r => p.failure?.id === r.id && <p role="alert" className="text-xs font-bold text-danger-700">{t(`board.error.${p.failure.error}`)}</p>} />
        </Section>
      )}
    </>
  );
  return (
    <PageContainer width="fluid">
      {/* No eyebrow repeating the menu item; the count is secondary text that arrives without moving the row (MDV-P2-059). */}
      <PageHeader title={t('nav.nurse')} meta={!p.loading && !p.forbidden && !p.offline && !p.failed && tCount('board.count', total)} />
      {body}
    </PageContainer>
  );
}

function Section({ id, title, children }: { id: string; title: string; children: React.ReactNode }) {
  return (
    // The table below is the named region (same name), so the wrapper is not a second one.
    <div className="flex flex-col gap-2">
      <h2 id={id} className="text-lg font-extrabold">{title}</h2>
      {children}
    </div>
  );
}
