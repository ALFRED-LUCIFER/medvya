import { Link } from 'react-router';
import { Syringe } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { buttonVariants } from '@medvya/ui';
import { minutesSince, type Board, type BoardCard, type LiveStatus, type MoveError } from '../../front-desk/domain/board';

type Props = {
  board: Board | null; cards: BoardCard[]; checkedIn: BoardCard[]; now: number; loading: boolean; offline: boolean; forbidden: boolean; failed: boolean; live: LiveStatus;
  pendingId: string | null; failure: { id: string; error: MoveError } | null;
  intakePath: (appointmentId: string) => string;
  onRetry: () => void;
  onTake: (appointmentId: string) => void;
};

/**
 * N-01 queue (MDV-P2-007): the board's "With nurse" cards, longest wait first, each opening its intake; below them the "Checked in" cards the
 * nurse may take to intake without waiting for reception (#956). Nothing clinical shows here.
 */
export default function NurseQueuePage(p: Props) {
  const { t, tCount } = useI18n();
  let body;
  if (p.forbidden) body = <State title={t('err.403')} body={t('board.forbidden.body')} />;
  else if (p.offline) body = <State title={t('state.offline.title')} body={t('board.offline')} />;
  else if (p.failed) body = <State title={t('board.error.title')} body={t('board.error.body')} action={<button type="button" className={buttonVariants({ variant: 'navy', className: 'min-h-11' })} onClick={p.onRetry}>{t('schedule.retry')}</button>} />;
  else if (p.loading || !p.board) body = <State busy title={t('intake.queue.loading')} />;
  else if (p.cards.length === 0 && p.checkedIn.length === 0) body = <State title={t('intake.queue.empty.title')} body={t('intake.queue.empty.body')} />;
  else body = (
    <>
      {p.cards.length > 0 && (
        <Section id="nq-with" title={t('intake.queue.withNurse')}>
          {p.cards.map(c => (
            <Card key={c.id} card={c} board={p.board!} waiting={t('intake.queue.waiting', { minutes: minutesSince(c.since, p.now) })}>
              <Link to={p.intakePath(c.id)} className={buttonVariants({ variant: 'navy', className: 'min-h-11 whitespace-normal' })} aria-label={t('intake.queue.openFor', { name: c.patientName || t('board.unknownPatient') })}>
                {t('intake.queue.open')}
              </Link>
            </Card>
          ))}
        </Section>
      )}
      {p.checkedIn.length > 0 && (
        <Section id="nq-in" title={t('intake.queue.checkedIn')}>
          {p.checkedIn.map(c => (
            <Card key={c.id} card={c} board={p.board!} waiting={t('board.waiting', { minutes: minutesSince(c.since, p.now) })}>
              <button type="button" disabled={p.pendingId === c.id} onClick={() => p.onTake(c.id)} className={buttonVariants({ variant: 'outline', className: 'min-h-11 whitespace-normal' })}
                aria-label={t('intake.queue.takeFor', { name: c.patientName || t('board.unknownPatient') })}>
                {t('intake.queue.take')}
              </button>
              {p.failure?.id === c.id && <p role="alert" className="text-xs font-bold text-danger-700">{t(`board.error.${p.failure.error}`)}</p>}
            </Card>
          ))}
        </Section>
      )}
    </>
  );
  return (
    <div className="flex flex-col gap-4">
      <header>
        <p className="inline-flex items-center gap-1.5 text-[13px] font-bold text-action-600"><Syringe aria-hidden className="size-4" />{t('nav.nurse')}</p>
        <h1 className="text-[26px] font-extrabold tracking-tight">{t('intake.queue.title')}</h1>
        {!p.loading && !p.forbidden && !p.offline && !p.failed && <p className="text-sm text-muted-foreground">{tCount('board.count', p.cards.length + p.checkedIn.length)}</p>}
      </header>
      {body}
    </div>
  );
}

function Section({ id, title, children }: { id: string; title: string; children: React.ReactNode }) {
  return (
    <section aria-labelledby={id} className="flex flex-col gap-2">
      <h2 id={id} className="text-lg font-extrabold">{title}</h2>
      <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">{children}</ul>
    </section>
  );
}

function Card({ card: c, board, waiting, children }: { card: BoardCard; board: Board; waiting: string; children: React.ReactNode }) {
  const { t, formatTime } = useI18n();
  return (
    <li>
      <article aria-labelledby={`nq-${c.id}`} className="flex flex-col gap-2 rounded-xl border bg-card p-4">
        <div className="flex justify-between gap-2">
          <h3 id={`nq-${c.id}`} className="text-base font-bold">{c.patientName || t('board.unknownPatient')}</h3>
          <span className="font-mono text-sm text-muted-foreground">{formatTime(new Date(c.start), board.timeZone)}</span>
        </div>
        <p className="text-sm text-muted-foreground">{t('board.meta', { doctor: c.calendarName || t('schedule.doctor'), type: t(`visitType.${c.visitType}`) })}</p>
        <p className="text-sm font-semibold">{waiting}{c.token ? ` · ${t('board.token', { token: ltr(c.token) })}` : ''}</p>
        {children}
      </article>
    </li>
  );
}

function State({ title, body, action, busy = false }: { title: string; body?: string; action?: React.ReactNode; busy?: boolean }) {
  return (
    <div role={busy ? 'status' : 'alert'} className="flex max-w-[560px] flex-col items-start gap-2 rounded-xl border bg-card p-6">
      <h2 className="text-lg font-extrabold">{title}</h2>
      {body && <p className="text-muted-foreground">{body}</p>}
      {action}
    </div>
  );
}
