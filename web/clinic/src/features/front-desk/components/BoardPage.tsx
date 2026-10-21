import type { ReactNode } from 'react';
import { CalendarCheck, CircleCheck, ClipboardCheck, DoorOpen, EyeOff, ReceiptText, Syringe, UserCheck, type LucideIcon } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, FrontDeskTag } from '@medvya/ui';
import { boardColumns, minutesSince, nextState, type Board, type BoardCard, type BoardColumn, type MoveError } from '../domain/board';

// Every column is words and an icon (never colour alone), following the Clinician App board (P-01).
const columnIcons: Record<BoardColumn, LucideIcon> = {
  Booked: CalendarCheck, CheckedIn: UserCheck, WithNurse: Syringe, ReadyForDoctor: ClipboardCheck, InConsult: DoorOpen, Billing: ReceiptText, Done: CircleCheck,
};

/** The action that moves a card out of each column (the design's CTA per column). Done has none. */
const actionKeys: Record<Exclude<BoardColumn, 'Done'>, MessageKey> = {
  Booked: 'board.action.checkIn', CheckedIn: 'board.action.toNurse', WithNurse: 'board.action.intakeDone',
  ReadyForDoctor: 'board.action.callIn', InConsult: 'board.action.consultDone', Billing: 'board.action.closeVisit',
};

type Props = {
  board: Board | null;
  columns: Record<BoardColumn, BoardCard[]>;
  /** Server-clock milliseconds, for wait timers. */
  now: number;
  loading: boolean;
  offline: boolean;
  online: boolean;
  forbidden: boolean;
  failed: boolean;
  /** Full "Check-in & queue" access; otherwise the board is read-only. */
  canMove: boolean;
  confirmingId: string | null;
  pendingId: string | null;
  failure: { id: string; error: MoveError } | null;
  onRetry: () => void;
  onStartCheckIn: (id: string) => void;
  onConfirmCheckIn: () => void;
  onCancelCheckIn: () => void;
  onMove: (id: string, state: BoardColumn) => void;
  /** "Register patient", shown when the role may register. */
  registerAction?: ReactNode;
};

/** P-01 Reception board: today's visits by state, one tap per step, nonclinical information only. */
export default function BoardPage(props: Props) {
  const { t, formatDay } = useI18n();
  const { board } = props;
  return (
    // A minmax(0, 1fr) track keeps the board's width out of the page's minimum width, so only the board scrolls sideways.
    <div className="grid grid-cols-[minmax(0,1fr)] gap-4">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="text-[13px] font-bold text-action-600">{t('nav.frontDesk')}</p>
          <h1 className="text-[26px] font-extrabold tracking-tight">{board ? t('board.title', { day: formatDay(board.date) }) : t('board.titleOnly')}</h1>
          {board && <p className="text-[13px] text-muted-foreground">{t('schedule.facilityTime', { zone: board.timeZone })}</p>}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <p className="inline-flex min-h-9 items-center gap-1.5 rounded-md bg-muted px-3 text-[12.5px] font-bold text-ink-700">
            <EyeOff aria-hidden className="size-4" />{t('board.hidesClinical')}
          </p>
          {props.registerAction}
        </div>
      </header>
      <Body {...props} />
    </div>
  );
}

function Message({ title, body, action, busy = false }: { title: string; body?: string; action?: { label: string; run: () => void }; busy?: boolean }) {
  return (
    <div role={busy ? 'status' : 'alert'} className="flex max-w-[560px] flex-col items-start gap-2 rounded-xl border bg-card p-6">
      <h2 className="text-lg font-extrabold">{title}</h2>
      {body && <p className="text-muted-foreground">{body}</p>}
      {action && <Button variant="navy" className="mt-2" onClick={action.run}>{action.label}</Button>}
    </div>
  );
}

function Body(props: Props) {
  const { t } = useI18n();
  const { board, forbidden, offline, failed, loading } = props;
  if (forbidden) return <Message title={t('err.403')} body={t('board.forbidden.body')} />;
  if (offline) return <Message title={t('state.offline.title')} body={t('board.offline')} />;
  if (failed) return <Message title={t('board.error.title')} body={t('board.error.body')} action={{ label: t('schedule.retry'), run: props.onRetry }} />;
  if (loading || !board) return <Message busy title={t('board.loading')} />;
  if (board.cards.length === 0) return <Message title={t('board.empty.title')} body={t('board.empty.body')} />;
  return (
    // The board scrolls sideways on its own at tablet width and 200 % zoom; the page never does.
    <section aria-label={t('board.region')} tabIndex={0} className="relative overflow-x-auto rounded-lg pb-2">
      <div className="grid auto-cols-[minmax(200px,1fr)] grid-flow-col gap-3">
        {boardColumns.map(c => <Column key={c} state={c} {...props} board={board} />)}
      </div>
    </section>
  );
}

function Column(props: Props & { state: BoardColumn; board: Board }) {
  const { t } = useI18n();
  const { state, columns } = props;
  const cards = columns[state], Icon = columnIcons[state];
  const id = `board-col-${state}`;
  return (
    <section aria-labelledby={id} className="flex min-h-[360px] flex-col gap-2 rounded-xl bg-muted p-2.5">
      <h2 id={id} className="flex items-center justify-between gap-2 px-1 pb-1 text-[13px] font-extrabold">
        <span className="flex items-center gap-1.5"><Icon aria-hidden className="size-4" />{t(`board.column.${state}`)}</span>
        <span className="font-mono text-xs font-semibold text-muted-foreground">
          <span aria-hidden>{cards.length}</span><span className="sr-only">{t('board.count', { count: cards.length })}</span>
        </span>
      </h2>
      {cards.length === 0 ? <p className="px-1 text-xs text-muted-foreground">{t('board.column.none')}</p> : (
        <ul className="flex flex-col gap-2">
          {cards.map(card => <li key={card.id}><Card card={card} {...props} /></li>)}
        </ul>
      )}
    </section>
  );
}

function Card({ card, board, now, canMove, online, confirmingId, pendingId, failure, onStartCheckIn, onConfirmCheckIn, onCancelCheckIn, onMove }: Props & { card: BoardCard; board: Board }) {
  const { t, formatTime, formatDate } = useI18n();
  const state = card.state as BoardColumn;
  const next = nextState(state);
  const busy = pendingId === card.id;
  const nameId = `board-card-${card.id}`;
  const error = failure?.id === card.id ? failure.error : null;
  const confirming = confirmingId === card.id;
  const dob = card.dateOfBirth ? formatDate(new Date(`${card.dateOfBirth}T12:00:00Z`)) : t('board.dobUnknown');

  return (
    <article aria-labelledby={nameId} className="flex flex-col gap-1.5 rounded-lg border bg-card px-3 py-2.5">
      <div className="flex justify-between gap-1.5">
        <h3 id={nameId} className="text-sm font-bold">{card.patientName || t('board.unknownPatient')}</h3>
        <span className="font-mono text-xs text-muted-foreground">{formatTime(new Date(card.start), board.timeZone)}</span>
      </div>
      <p className="text-xs text-muted-foreground">{t('board.meta', { doctor: card.calendarName || t('schedule.doctor'), type: t(`visitType.${card.visitType}`) })}</p>
      <div className="flex flex-wrap gap-1">
        <FrontDeskTag kind="reason" text={t(`reasonCategory.${card.reasonCategory}`)} />
        {card.registrationIncomplete && <FrontDeskTag kind="incomplete" />}
      </div>
      {state === 'CheckedIn' && <FrontDeskTag kind="waiting" text={t('board.waiting', { minutes: minutesSince(card.since, now) })} />}

      {canMove && confirming ? (
        <div role="group" aria-label={t('board.confirm.title')} className="mt-1 flex flex-col gap-2 rounded-md border border-action-600 p-2">
          <p className="text-xs font-bold">{t('board.confirm.title')}</p>
          <p className="text-sm">{t('board.confirm.identity', { name: card.patientName || t('board.unknownPatient'), dob })}</p>
          {card.registrationIncomplete && <p className="text-xs text-warning-700">{t('board.confirm.incomplete')}</p>}
          <Button autoFocus className="whitespace-normal" loading={busy} disabled={!online} onClick={onConfirmCheckIn}>{t('board.confirm.arrived')}</Button>
          <Button variant="outline" onClick={onCancelCheckIn}>{t('booking.cancel')}</Button>
        </div>
      ) : canMove && next && state !== 'Done' ? (
        <Button variant="outline" className="mt-1 whitespace-normal" loading={busy} disabled={!online}
          onClick={() => (state === 'Booked' ? onStartCheckIn(card.id) : onMove(card.id, next))}>
          {t(actionKeys[state])}
        </Button>
      ) : null}
      {canMove && state === 'Billing' && (
        <>
          {/* shortcut: payment is a placeholder until invoices and payments land (MDV-P4 billing, flag billing.invoices). */}
          <Button variant="outline" className="whitespace-normal" disabled aria-describedby={`${nameId}-pay`}>{t('board.action.paymentReceived')}</Button>
          <p id={`${nameId}-pay`} className="text-xs text-muted-foreground">{t('board.paymentLater')}</p>
        </>
      )}
      {error && <p role="alert" className="text-xs font-bold text-danger-700">{t(`board.error.${error}`)}</p>}
    </article>
  );
}
