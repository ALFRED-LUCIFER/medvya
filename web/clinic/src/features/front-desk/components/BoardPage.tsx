import { useRef, type ReactNode } from 'react';
import { Link } from 'react-router';
import type { UseFormReturn } from 'react-hook-form';
import { CalendarCheck, CalendarClock, CircleCheck, ClipboardCheck, DoorOpen, Ellipsis, EyeOff, Printer, ReceiptText, Syringe, Undo2, UserCheck, type LucideIcon } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, buttonVariants, Dialog, DialogContent, DialogDescription, DialogTitle, FrontDeskTag, PageContainer, PageHeader } from '@medvya/ui';
import type { CancelForm } from '../../schedule/domain/changes';
import CancelReasonFields from '../../schedule/components/CancelReasonFields';
import type { workload } from '../domain/overview';
import { ageYears, canReprint, endKinds, minutesSince, needsRebooking, nextState, tokenSlipPath, visibleColumns, type Board, type BoardCard, type BoardColumn, type EndKind, type LiveStatus, type MoveError, type Notice, type VisitSteps } from '../domain/board';

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
  /** The visit steps the practice has on: a step that is off has no column and is skipped (MDV audit UX-05). */
  steps: VisitSteps;
  columns: Record<BoardColumn, BoardCard[]>;
  /** Visits with a message to the patient that failed or bounced (MDV-P1-034). */
  failedVisitIds: ReadonlySet<string>;
  /** Server-clock milliseconds, for wait timers. */
  now: number;
  loading: boolean;
  offline: boolean;
  online: boolean;
  /** Real-time connection (MDV-P1-029); while not live the board refreshes every 30 s. */
  live: LiveStatus;
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
  /** What this desk last did (MDV-P1-100): said in the status line, with Undo while `undo` is set. */
  notice: Notice | null;
  undoing: boolean;
  onUndo: () => void;
  /** May cancel a visit (full "Register / book patients"); marking a patient as left needs only `canMove`. */
  canCancel: boolean;
  /** The card being ended from its menu; kind null while the desk chooses between left and cancel. */
  ending: { card: BoardCard; kind: EndKind | null } | null;
  endForm: UseFormReturn<CancelForm>;
  endWorking: boolean;
  endFailed: boolean;
  onStartEnd: (card: BoardCard, kind: EndKind | null) => void;
  onEndKind: (kind: EndKind) => void;
  onEndReason: (reason: CancelForm['reason']) => void;
  onConfirmEnd: () => void;
  onCancelEnd: () => void;
  /** "Register patient", shown when the role may register. */
  registerAction?: ReactNode;
  /** Link to the waitlist. */
  overviewAction?: ReactNode;
  /** The board's view switch (board / doctors, MDV audit UX-05). */
  views?: ReactNode;
  workload: ReturnType<typeof workload>;
  /** Payment for Billing cards: present when billing is on (flag billing.invoices, P4); until then a disabled placeholder. */
  paymentAction?: ReactNode;
  /** "Start consultation" for a card (MDV-P2-001), for doctors; null for a card it does not apply to. */
  consultAction?: (card: BoardCard) => ReactNode;
  /** With the nurse-intake step on (MDV-P2-007) a With nurse card leaves the nurse by the nurse's handoff, not by this board's move: shown instead of "Intake done". */
  nurseStep?: (card: BoardCard) => ReactNode;
  /** On a Done card with the doctor's follow-up still to book: "Follow-up due" with Book (MDV-P1-102). */
  followUp?: (card: BoardCard) => ReactNode;
  /** The follow-up's dismiss dialog, one for the board. */
  followUpDialog?: ReactNode;
};

/** P-01 Reception board: today's visits by state, one tap per step, nonclinical information only. */
export default function BoardPage(props: Props) {
  const { t, formatDay } = useI18n();
  const { board } = props;
  return (
    // A minmax(0, 1fr) track keeps the board's width out of the page's minimum width, so only the board scrolls sideways.
    <PageContainer width="fluid">
      <PageHeader title={board ? t('board.title', { day: formatDay(board.date) }) : t('board.titleOnly')}
        description={board && <span className="text-[13px]">{t('schedule.facilityTime', { zone: board.timeZone })}</span>}
        actions={<>
          {/* Live or paused: announced when it changes, in words and an icon, never colour alone. Nothing animates. */}
          {!props.forbidden && <p role="status" className="inline-flex min-h-9 items-center"><FrontDeskTag kind={props.live} /></p>}
          <p className="inline-flex min-h-9 items-center gap-1.5 rounded-md bg-muted px-3 text-[12.5px] font-bold text-ink-700">
            <EyeOff aria-hidden className="size-4" />{t('board.hidesClinical')}
          </p>
          {props.overviewAction}
          {props.registerAction}
        </>}>
        {props.views}
      </PageHeader>
      {props.board && props.workload.length > 0 && <Workload rows={props.workload} />}
      <StatusLine {...props} />
      <Body {...props} />
      <EndVisit {...props} />
      {props.followUpDialog}
    </PageContainer>
  );
}

/** What this desk last did, announced when it changes; Undo sits beside it for 30 seconds (the server refuses later). */
function StatusLine({ board, notice, undoing, online, canMove, onUndo }: Props) {
  const { t } = useI18n();
  const left = board?.leftWithoutBeingSeen ?? 0;
  return (
    <div className="flex min-h-11 flex-wrap items-center gap-x-3 gap-y-1 text-sm">
      <p role="status" className="font-bold">
        {notice && t(`board.notice.${notice.kind}`, { name: notice.name || t('board.unknownPatient'), column: t(`board.column.${notice.state}`) })}
      </p>
      {notice?.undo && canMove && <Button variant="outline" loading={undoing} disabled={!online} onClick={onUndo}><Undo2 aria-hidden />{t('board.undo')}</Button>}
      {left > 0 && <p className="ms-auto text-muted-foreground">{t('board.leftToday', { count: left })}</p>}
    </div>
  );
}

/** The card menu's dialog: left without being seen or cancel, then the reason. Closing it changes nothing. */
function EndVisit({ ending, endForm, endWorking, endFailed, online, onEndKind, onEndReason, onConfirmEnd, onCancelEnd }: Props) {
  const { t } = useI18n();
  const name = ending?.card.patientName || t('board.unknownPatient');
  // The dialog closes after `ending` is cleared, so remember whose menu opened it to give focus back there.
  const opener = useRef<string | null>(null);
  return (
    <Dialog open={ending !== null} onOpenChange={open => { if (!open && !endWorking) onCancelEnd(); }}>
      <DialogContent closeLabel={t('board.end.close')} onOpenAutoFocus={() => { opener.current = ending?.card.id ?? null; }}
        // Opened by state, not by a trigger: give focus back to the card's menu button, or to the board when the card has left it.
        onCloseAutoFocus={e => { e.preventDefault(); (document.getElementById(`board-more-${opener.current}`) ?? document.getElementById('board-region'))?.focus(); }}>
        <DialogTitle>{t(ending?.kind ? `board.end.${ending.kind}.title` : 'board.end.title', { name })}</DialogTitle>
        {ending?.kind ? (
          <form noValidate className="flex flex-col gap-3" onSubmit={e => { e.preventDefault(); onConfirmEnd(); }}>
            <DialogDescription>{t(`board.end.${ending.kind}.body`)}</DialogDescription>
            <CancelReasonFields form={endForm} onReason={onEndReason} />
            {endFailed && <p role="alert" className="text-sm font-bold text-danger-700">{t('board.error.failed')}</p>}
            <div className="flex flex-wrap gap-2">
              <Button type="submit" loading={endWorking} disabled={!online}>{t(`board.end.${ending.kind}.confirm`)}</Button>
              <Button type="button" variant="outline" disabled={endWorking} onClick={onCancelEnd}>{t('appointment.back')}</Button>
            </div>
          </form>
        ) : (
          <>
            <DialogDescription>{t('board.end.choose')}</DialogDescription>
            <div className="flex flex-col gap-2">
              <Button variant="outline" className="whitespace-normal" onClick={() => onEndKind('left')}>{t('board.end.left')}</Button>
              <Button variant="outline" className="whitespace-normal" onClick={() => onEndKind('cancel')}>{t('board.end.cancel')}</Button>
            </div>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}

/** P-01 "Doctor workload": per doctor, the number waiting in words and a bar for the share of the longest queue. */
function Workload({ rows }: { rows: Props['workload'] }) {
  const { t } = useI18n();
  return (
    <section aria-labelledby="board-workload" className="flex flex-col gap-2 rounded-xl border bg-card p-4">
      <h2 id="board-workload" className="text-[15px] font-extrabold">{t('overview.workload')}</h2>
      <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {rows.map(r => (
          <li key={r.calendarId} className="flex flex-col gap-1">
            <span className="flex flex-wrap justify-between gap-2 text-[13.5px]">
              <strong>{r.name}</strong>
              <span className="text-muted-foreground">
                {[t('overview.waitingCount', { count: r.waiting }), r.delayMinutes > 0 ? t('overview.late', { minutes: r.delayMinutes }) : t('overview.onTime')].join(' · ')}
              </span>
            </span>
            <span aria-hidden className="h-2 overflow-hidden rounded bg-muted"><span className="block h-full rounded bg-action-600" style={{ width: `${Math.round(r.share * 100)}%` }} /></span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function Message({ title, body, action, busy = false }: { title: string; body?: string; action?: { label: string; run: () => void }; busy?: boolean }) {
  return (
    <div role={busy ? 'status' : 'alert'} className="flex max-w-[560px] flex-col items-start gap-2 rounded-xl border bg-card p-6">
      <h2 className="text-lg font-extrabold">{title}</h2>
      {body && <p className="text-muted-foreground">{body}</p>}
      {action && <Button variant="default" className="mt-2" onClick={action.run}>{action.label}</Button>}
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
    <section id="board-region" aria-label={t('board.region')} tabIndex={0} className="relative overflow-x-auto rounded-lg pb-2">
      <div className="grid auto-cols-[minmax(156px,1fr)] grid-flow-col gap-3">
        {visibleColumns(props.steps, props.columns).map(c => <Column key={c} state={c} {...props} board={board} />)}
      </div>
      <Rebooking cards={needsRebooking(board.cards)} timeZone={board.timeZone} />
    </section>
  );
}

/** Visits the doctor's leave moved out of their slot: words and an icon, and where to fix them. */
function Rebooking({ cards, timeZone }: { cards: BoardCard[]; timeZone: string }) {
  const { t, formatTime } = useI18n();
  if (cards.length === 0) return null;
  return (
    <section aria-labelledby="board-rebooking" className="mt-3 flex flex-col gap-2 rounded-xl border border-dashed bg-card p-3">
      <h2 id="board-rebooking" className="flex items-center gap-1.5 text-[13px] font-extrabold"><CalendarClock aria-hidden className="size-4" />{t('board.rebooking.title')}</h2>
      <p className="text-sm text-muted-foreground">{t('board.rebooking.body')}</p>
      <ul className="flex flex-col gap-1">
        {cards.map(c => <li key={c.id} className="text-[13.5px]"><strong>{c.patientName}</strong>{' · '}{c.calendarName}{' · '}{t('board.rebooking.was', { time: formatTime(new Date(c.start), timeZone) })}</li>)}
      </ul>
    </section>
  );
}

function Column(props: Props & { state: BoardColumn; board: Board }) {
  const { t, tCount } = useI18n();
  const { state, columns } = props;
  const cards = columns[state], Icon = columnIcons[state];
  const id = `board-col-${state}`;
  return (
    <section aria-labelledby={id} className="flex min-h-[360px] flex-col gap-2 rounded-xl bg-muted p-2.5">
      <h2 id={id} className="flex items-center justify-between gap-2 px-1 pb-1 text-[13px] font-extrabold">
        <span className="flex items-center gap-1.5"><Icon aria-hidden className="size-4" />{t(`board.column.${state}`)}</span>
        <span className="font-mono text-xs font-semibold text-muted-foreground">
          <span aria-hidden>{cards.length}</span><span className="sr-only">{tCount('board.count', cards.length)}</span>
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

function Card({ card, board, steps, now, failedVisitIds, canMove, canCancel, online, confirmingId, pendingId, failure, paymentAction, consultAction, nurseStep, followUp, onStartCheckIn, onConfirmCheckIn, onCancelCheckIn, onMove, onStartEnd }: Props & { card: BoardCard; board: Board }) {
  const { t, formatTime, formatCalendarDate } = useI18n();
  const state = card.state as BoardColumn;
  const next = nextState(state, steps);
  const busy = pendingId === card.id;
  const nameId = `board-card-${card.id}`;
  const error = failure?.id === card.id ? failure.error : null;
  const confirming = confirmingId === card.id;
  const age = ageYears(card.dateOfBirth, now);
  const dob = card.dateOfBirth ? formatCalendarDate(card.dateOfBirth) : t('board.dobUnknown');
  const ends = canMove ? endKinds(card.state, canCancel) : [];

  return (
    <article aria-labelledby={nameId} className="flex flex-col gap-1.5 rounded-lg border bg-card px-3 py-2.5">
      <div className="flex justify-between gap-1.5">
        <h3 id={nameId} className="text-sm font-bold">{card.patientName || t('board.unknownPatient')}</h3>
        <span className="font-mono text-xs text-muted-foreground">{formatTime(new Date(card.start), board.timeZone)}</span>
      </div>
      {age !== null && <p className="text-xs text-muted-foreground">{t('patients.row.ageSex', { age: t('patients.age', { age }), sex: card.sex ? t(`patients.sex.${card.sex}`) : '' })}</p>}
      <div className="flex flex-wrap gap-1">
        {failedVisitIds.has(card.id) && <FrontDeskTag kind="messageFailed" />}
        {card.walkIn && <FrontDeskTag kind="walkIn" />}
        {card.registrationIncomplete && <FrontDeskTag kind="incomplete" />}
        {card.token && <FrontDeskTag kind="token" text={t('board.token', { token: card.token })} />}
      </div>
      {(state === 'CheckedIn' || state === 'ReadyForDoctor') && <FrontDeskTag kind="waiting" text={t('board.waiting', { minutes: minutesSince(card.since, now) })} />}
      {card.room && state === 'InConsult' && <FrontDeskTag kind="room" text={t('board.room', { room: card.room })} />}

      {canMove && confirming ? (
        <div role="group" aria-label={t('board.confirm.title')} className="mt-1 flex flex-col gap-2 rounded-md border border-action-600 p-2">
          <p className="text-xs font-bold">{t('board.confirm.title')}</p>
          <p className="text-sm">{t('board.confirm.identity', { name: card.patientName || t('board.unknownPatient'), dob })}</p>
          {card.registrationIncomplete && <p className="text-xs text-warning-700">{t('board.confirm.incomplete')}</p>}
          <Button autoFocus className="whitespace-normal" loading={busy} disabled={!online} onClick={onConfirmCheckIn}>{t('board.confirm.arrived')}</Button>
          <Button variant="outline" onClick={onCancelCheckIn}>{t('booking.cancel')}</Button>
        </div>
      ) : nurseStep && state === 'WithNurse' ? nurseStep(card) : card.withDoctor && state === 'InConsult' ? (
        // A consultation is open: the doctor's End moves the card, so the desk has words here and no action (MDV-P1-100).
        <FrontDeskTag kind="room" text={t('board.withDoctor')} />
      ) : canMove && next && state !== 'Done' ? (
        <Button variant="outline" className="mt-1 whitespace-normal" loading={busy} disabled={!online}
          onClick={() => (state === 'Booked' ? onStartCheckIn(card.id) : onMove(card.id, next))}>
          {t(state === 'CheckedIn' && next === 'ReadyForDoctor' ? 'board.action.toDoctor' : actionKeys[state])}
        </Button>
      ) : null}
      {/* shortcut: with billing.invoices off (all of P1) payment is a disabled placeholder; P4 builds payments behind that flag. */}
      {canMove && state === 'Billing' && (paymentAction ?? (
        <>
          <Button variant="outline" className="whitespace-normal" disabled aria-describedby={`${nameId}-pay`}>{t('board.action.paymentReceived')}</Button>
          <p id={`${nameId}-pay`} className="text-xs text-muted-foreground">{t('board.paymentLater')}</p>
        </>
      ))}
      {consultAction?.(card)}
      {state === 'Done' && card.followUp && followUp?.(card)}
      {/* MDV-P2-046: the same token again, for a lost slip; a void token is off the board and the server refuses it anyway. */}
      {canMove && canReprint(card) && (
        <Link to={tokenSlipPath(card.id)} className={buttonVariants({ variant: 'ghost', className: 'whitespace-normal' })}>
          <Printer aria-hidden />{t('tokenSlip.reprint', { token: card.token! })}
        </Link>
      )}
      {ends.length > 0 && (
        <Button id={`board-more-${card.id}`} variant="ghost" className="whitespace-normal" disabled={!online}
          aria-label={t('board.more.for', { name: card.patientName || t('board.unknownPatient') })} onClick={() => onStartEnd(card, ends.length === 1 ? ends[0]! : null)}>
          <Ellipsis aria-hidden />{t('board.more')}
        </Button>
      )}
      {error && <p role="alert" className="text-xs font-bold text-danger-700">{t(`board.error.${error}`)}</p>}
    </article>
  );
}
