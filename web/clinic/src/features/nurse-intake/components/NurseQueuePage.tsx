import { Link } from 'react-router';
import { Syringe } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { buttonVariants } from '@medvya/ui';
import { minutesSince, type Board, type BoardCard, type LiveStatus } from '../../front-desk/domain/board';

type Props = {
  board: Board | null; cards: BoardCard[]; now: number; loading: boolean; offline: boolean; forbidden: boolean; failed: boolean; live: LiveStatus;
  intakePath: (appointmentId: string) => string;
  onRetry: () => void;
};

/** N-01 queue (MDV-P2-007): the board's "With nurse" cards, longest wait first, each opening its intake. Nothing clinical shows here. */
export default function NurseQueuePage(p: Props) {
  const { t, tCount, formatTime } = useI18n();
  let body;
  if (p.forbidden) body = <State title={t('err.403')} body={t('board.forbidden.body')} />;
  else if (p.offline) body = <State title={t('state.offline.title')} body={t('board.offline')} />;
  else if (p.failed) body = <State title={t('board.error.title')} body={t('board.error.body')} action={<button type="button" className={buttonVariants({ variant: 'navy', className: 'min-h-11' })} onClick={p.onRetry}>{t('schedule.retry')}</button>} />;
  else if (p.loading || !p.board) body = <State busy title={t('intake.queue.loading')} />;
  else if (p.cards.length === 0) body = <State title={t('intake.queue.empty.title')} body={t('intake.queue.empty.body')} />;
  else body = (
    <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
      {p.cards.map(c => (
        <li key={c.id}>
          <article aria-labelledby={`nq-${c.id}`} className="flex flex-col gap-2 rounded-xl border bg-card p-4">
            <div className="flex justify-between gap-2">
              <h2 id={`nq-${c.id}`} className="text-base font-bold">{c.patientName || t('board.unknownPatient')}</h2>
              <span className="font-mono text-sm text-muted-foreground">{formatTime(new Date(c.start), p.board!.timeZone)}</span>
            </div>
            <p className="text-sm text-muted-foreground">{t('board.meta', { doctor: c.calendarName || t('schedule.doctor'), type: t(`visitType.${c.visitType}`) })}</p>
            <p className="text-sm font-semibold">{t('intake.queue.waiting', { minutes: minutesSince(c.since, p.now) })}{c.token ? ` · ${t('board.token', { token: ltr(c.token) })}` : ''}</p>
            <Link to={p.intakePath(c.id)} className={buttonVariants({ variant: 'navy', className: 'min-h-11 whitespace-normal' })} aria-label={t('intake.queue.openFor', { name: c.patientName || t('board.unknownPatient') })}>
              {t('intake.queue.open')}
            </Link>
          </article>
        </li>
      ))}
    </ul>
  );
  return (
    <div className="flex flex-col gap-4">
      <header>
        <p className="inline-flex items-center gap-1.5 text-[13px] font-bold text-action-600"><Syringe aria-hidden className="size-4" />{t('nav.nurse')}</p>
        <h1 className="text-[26px] font-extrabold tracking-tight">{t('intake.queue.title')}</h1>
        {!p.loading && !p.forbidden && !p.offline && !p.failed && <p className="text-sm text-muted-foreground">{tCount('board.count', p.cards.length)}</p>}
      </header>
      {body}
    </div>
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
