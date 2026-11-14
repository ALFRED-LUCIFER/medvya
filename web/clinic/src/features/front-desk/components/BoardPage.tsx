import { useRef, type ReactNode } from 'react';
import { Link } from 'react-router';
import type { UseFormReturn } from 'react-hook-form';
import { CalendarClock, Ellipsis, EyeOff, Printer, Undo2 } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, buttonVariants, cn, Dialog, DialogContent, DialogDescription, DialogTitle, FrontDeskTag, PageContainer, PageHeader, SegmentedControl, statusIcons } from '@medvya/ui';
import { isDeviceZone, type CancelForm } from '../../schedule/domain/changes';
import CancelReasonFields from '../../schedule/components/CancelReasonFields';
import QueueTable from './QueueTable';
import type { workload } from '../domain/overview';
import { aheadWords, ageYears, canReprint, endKinds, listGroups, minutesSince, needsRebooking, nextConsultId, nextState, tokenSlipPath, visibleColumns, type Board, type BoardCard, type BoardColumn, type EndKind, type ListRow, type LiveStatus, type MoveError, type Notice, type VisitSteps } from '../domain/board';

// Every column is words and an icon (never colour alone), from the shared status map (MDV-P5-057).

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
  /** "Start consultation" for a card (MDV-P2-001), for doctors; null for a card it does not apply to. `primary` on the one next patient. */
  consultAction?: (card: BoardCard, primary: boolean) => ReactNode;
  /** With the nurse-intake step on (MDV-P2-007) a With nurse card leaves the nurse by the nurse's handoff, not by this board's move: shown instead of "Intake done". */
  nurseStep?: (card: BoardCard) => ReactNode;
  /** On a Done card with the doctor's follow-up still to book: "Follow-up due" with Book (MDV-P1-102). */
  followUp?: (card: BoardCard) => ReactNode;
  /** The follow-up's dismiss dialog, one for the board. */
  followUpDialog?: ReactNode;
  /** Under 768 px the board is one list: the status it shows (null: the first with visits). */
  phoneColumn: BoardColumn | null;
  onPhoneColumn: (column: BoardColumn) => void;
  /** The List view (MDV-P2-052) instead of the columns. */
  list?: boolean;
  /** The status the list shows alone (its pressed count); null: every status with visits. */
  listFilter: BoardColumn | null;
  onListFilter: (column: BoardColumn) => void;
};

/** P-01 Reception board: today's visits by state, one tap per step, nonclinical information only. */
export default function BoardPage(props: Props) {
  const { t, formatShortDay } = useI18n();
  const { board } = props;
  // The facility's zone is said only when this device is elsewhere; times are always the facility's.
  const zone = board && !isDeviceZone(board.timeZone) ? t('schedule.facilityTime', { zone: board.timeZone }) : null;
  return (
    // A minmax(0, 1fr) track keeps the board's width out of the page's minimum width, so only the board scrolls sideways.
    <PageContainer width="fluid" className="gap-2">
      {/* One 44 px row (MDV-P1-095): title and short date, the view switch, the live mark, then the actions. */}
      <PageHeader title={board ? t('board.title', { day: formatShortDay(board.date) }) : t('board.titleOnly')} meta={zone}
        actions={<>
          {props.views}
          {props.overviewAction}
          {props.registerAction}
        </>} />
      <StatusLine {...props} />
      <Body {...props} />
      {!props.consultAction && <p className="inline-flex items-center gap-1.5 text-caption text-muted-foreground"><EyeOff aria-hidden className="size-4" />{t('board.hidesClinical')}</p>}
      <EndVisit {...props} />
      {props.followUpDialog}
    </PageContainer>
  );
}

/**
 * One line under the header: each doctor's workload, then what this desk last did (announced when it changes) with Undo beside it
 * for 30 seconds (the server refuses later), today's left-without-being-seen count and the live mark.
 */
function StatusLine({ board, notice, undoing, online, canMove, onUndo, workload, live, forbidden }: Props) {
  const { t } = useI18n();
  const left = board?.leftWithoutBeingSeen ?? 0;
  return (
    // A line of text, so it costs 20 px; Undo makes it a control's height while it is offered.
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-body">
      {board && workload.length > 0 && <Workload rows={workload} />}
      <div className="ms-auto flex flex-wrap items-center gap-x-3 gap-y-1">
        <p role="status" className="font-bold">
          {notice && t(`board.notice.${notice.kind}`, { name: notice.name || t('board.unknownPatient'), column: t(`board.column.${notice.state}`) })}
        </p>
        {notice?.undo && canMove && <Button variant="outline" loading={undoing} disabled={!online} onClick={onUndo}><Undo2 aria-hidden />{t('board.undo')}</Button>}
        {left > 0 && <p className="text-muted-foreground">{t('board.leftToday', { count: left })}</p>}
        {/* Live or paused: announced when it changes, in words and an icon, never colour alone. Nothing animates. */}
        {!forbidden && <p role="status" className="inline-flex items-center"><FrontDeskTag kind={live} /></p>}
      </div>
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

/** P-01 "Doctor workload" on one line (MDV-P1-095): per doctor, the number waiting and how late they run, in words. */
function Workload({ rows }: { rows: Props['workload'] }) {
  const { t } = useI18n();
  return (
    <section aria-labelledby="board-workload" className="min-w-0">
      <h2 id="board-workload" className="sr-only">{t('overview.workload')}</h2>
      <ul className="flex flex-wrap gap-x-4 gap-y-1">
        {rows.map(r => (
          <li key={r.calendarId}>
            <strong>{r.name}</strong>{' '}
            <span className="text-muted-foreground">
              {[t('overview.waitingCount', { count: r.waiting }), r.delayMinutes > 0 ? t('overview.late', { minutes: r.delayMinutes }) : t('overview.onTime')].join(' · ')}
            </span>
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
  if (props.list) return <List {...props} board={board} />;
  const shown = visibleColumns(props.steps, props.columns);
  const phone = props.phoneColumn && shown.includes(props.phoneColumn) ? props.phoneColumn : (shown.find(c => props.columns[c].length > 0) ?? shown[0]!);
  const primary = props.consultAction ? nextConsultId(props.columns) : null;
  return (
    <>
      {/* Under 768 px the board is one list, chosen by status; the page never scrolls sideways. */}
      <SegmentedControl label={t('board.statusTabs')} className="flex-wrap md:hidden" value={phone} onValueChange={props.onPhoneColumn}
        options={shown.map(c => ({ value: c, icon: statusIcons[c], label: t('board.statusTab', { column: t(`board.column.${c}`), count: props.columns[c].length }) }))} />
      {/* From 768 px the board scrolls on its own, both ways, so each column's header stays in view; scroll padding keeps focus clear of it. */}
      <section id="board-region" aria-label={t('board.region')} tabIndex={0}
        className="relative rounded-lg md:max-h-[max(20rem,calc(100dvh-12rem))] md:scroll-pt-12 md:overflow-auto">
        <div className="flex flex-col gap-3 md:flex-row">
          {shown.map(c => <Column key={c} state={c} hiddenOnPhone={c !== phone} primary={primary} {...props} board={board} />)}
        </div>
        <Rebooking cards={needsRebooking(board.cards)} timeZone={board.timeZone} />
      </section>
    </>
  );
}

/**
 * P-01 as one list (MDV-P2-052): about nine patients where the board shows one and a half cards. Status counts filter it to one group;
 * groups run from who needs the desk first to booked, with done folded away. Rows are comfortable (48 px), never compact.
 */
function List(props: Props & { board: Board }) {
  const { t, tCount } = useI18n();
  const { board, columns, listFilter, onListFilter, failedVisitIds, confirmingId, failure, canMove, consultAction } = props;
  const { statuses, groups } = listGroups(props.steps, columns, listFilter);
  const primary = consultAction ? nextConsultId(columns) : null;
  const confirming = (r: ListRow) => canMove && confirmingId === r.id;
  const heading = (state: BoardColumn) => {
    const Icon = columnIcons[state], count = columns[state].length;
    return (
      <h2 className="flex items-center gap-1.5 text-label">
        <Icon aria-hidden className="size-4" />{t(`board.column.${state}`)}
        <span className="font-mono text-caption text-muted-foreground tabular-nums"><span aria-hidden>{count}</span><span className="sr-only">{tCount('board.count', count)}</span></span>
      </h2>
    );
  };
  const rowsOf = (state: BoardColumn, rows: ListRow[]) => rows.length === 0 ? <p className="text-caption text-muted-foreground">{t('board.column.none')}</p> : (
    <QueueTable label={t(`board.column.${state}`)} rows={rows} timeZone={board.timeZone} now={props.now}
      flags={r => <>
        {failedVisitIds.has(r.id) && <FrontDeskTag kind="messageFailed" />}
        {r.walkIn && <FrontDeskTag kind="walkIn" />}
        {r.registrationIncomplete && <FrontDeskTag kind="incomplete" />}
      </>}
      // While the identity check is open it is the row's action, across the full width under it.
      action={r => !confirming(r) && <div className="flex flex-wrap justify-end gap-2"><NextAction {...props} card={r} />{consultAction?.(r, r.id === primary)}</div>}
      detail={r => {
        const error = failure?.id === r.id ? failure.error : null;
        return (confirming(r) || error) && <>
          {confirming(r) && <ConfirmCheckIn {...props} card={r} />}
          {error && <p role="alert" className="text-xs font-bold text-danger-700">{t(`board.error.${error}`)}</p>}
        </>;
      }} />
  );
  return (
    <>
      <div role="group" aria-label={t('board.list.filters')} className="flex flex-wrap gap-2">
        {statuses.map(c => {
          const Icon = columnIcons[c];
          return (
            <Button key={c} variant={listFilter === c ? 'selected' : 'outline'} aria-pressed={listFilter === c} onClick={() => onListFilter(c)}>
              <Icon aria-hidden />{t('board.statusTab', { column: t(`board.column.${c}`), count: columns[c].length })}
            </Button>
          );
        })}
      </div>
      {groups.map(({ state, rows }) => state === 'Done' ? (
        // Folded unless Done is the pressed count; the key opens it afresh when the filter changes.
        <details key={`${state}-${listFilter}`} open={listFilter === 'Done'} className="flex flex-col gap-2">
          <summary className="flex min-h-11 cursor-pointer items-center">{heading(state)}</summary>
          {rowsOf(state, rows)}
        </details>
      ) : (
        <section key={state} className="flex flex-col gap-2">
          {heading(state)}
          {rowsOf(state, rows)}
        </section>
      ))}
      <Rebooking cards={needsRebooking(board.cards)} timeZone={board.timeZone} />
    </>
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

function Column(props: Props & { state: BoardColumn; board: Board; hiddenOnPhone: boolean; primary: string | null }) {
  const { t, tCount } = useI18n();
  const { state, columns } = props;
  const cards = columns[state], Icon = statusIcons[state];
  const id = `board-col-${state}`;
  const empty = cards.length === 0;
  return (
    // At least 208 px wide; from 768 px an empty column is a 48 px stub with its name and count, written top to bottom.
    <section aria-labelledby={id} className={cn('flex flex-col gap-2 rounded-xl bg-muted px-2.5 pb-2.5 md:min-h-90', props.hiddenOnPhone && 'max-md:hidden',
      empty ? 'md:w-12 md:flex-none md:items-center md:px-1' : 'md:flex-[1_0_13rem]')}>
      {/* Pinned while the board scrolls: the name and count stay in view (MDV-P1-095). */}
      <h2 id={id} className={cn('sticky top-0 z-10 flex items-center justify-between gap-2 bg-muted px-1 pt-2.5 pb-1 text-label', empty && 'md:[writing-mode:vertical-rl]')}>
        <span className="flex items-center gap-1.5"><Icon aria-hidden className="size-4" />{t(`board.column.${state}`)}</span>
        <span className="font-mono text-caption text-muted-foreground tabular-nums">
          <span aria-hidden>{cards.length}</span><span className="sr-only">{tCount('board.count', cards.length)}</span>
        </span>
      </h2>
      {empty ? <p className="px-1 text-caption text-muted-foreground md:hidden">{t('board.column.none')}</p> : (
        <ul className="flex flex-col gap-2">
          {cards.map(card => <li key={card.id}><Card card={card} {...props} /></li>)}
        </ul>
      )}
    </section>
  );
}

function Card(props: Props & { card: BoardCard; board: Board; primary: string | null }) {
  const { card, primary, board, now, workload, failedVisitIds, canMove, canCancel, online, confirmingId, failure, paymentAction, consultAction, followUp, onStartEnd } = props;
  const i18n = useI18n(), { t, formatTime } = i18n;
  const state = card.state as BoardColumn;
  const nameId = `board-card-${card.id}`;
  const error = failure?.id === card.id ? failure.error : null;
  const confirming = confirmingId === card.id;
  const age = ageYears(card.dateOfBirth, now);
  const ends = canMove ? endKinds(card.state, canCancel) : [];
  // The doctor only when more than one works today, so two doctors' patients are told apart (MDV-P1-095).
  const who = [age !== null && t('patients.age', { age }), age !== null && card.sex && t(`patients.sex.${card.sex}`), workload.length > 1 && card.calendarName].filter(Boolean);

  return (
    <article aria-labelledby={nameId} className="flex min-w-0 flex-col gap-1.5 rounded-lg border bg-card p-3">
      <div className="flex justify-between gap-1.5">
        <h3 id={nameId} className="min-w-0 break-words text-label">{card.patientName || t('board.unknownPatient')}</h3>
        <span className="shrink-0 font-mono text-caption text-muted-foreground tabular-nums">{formatTime(new Date(card.start), board.timeZone)}</span>
      </div>
      {who.length > 0 && <p className="break-words text-caption text-muted-foreground">{who.join(' · ')}</p>}
      <div className="flex flex-wrap gap-1">
        {failedVisitIds.has(card.id) && <FrontDeskTag kind="messageFailed" />}
        {card.walkIn && <FrontDeskTag kind="walkIn" />}
        {card.registrationIncomplete && <FrontDeskTag kind="incomplete" />}
        {/* The token and, while waiting, how many are ahead in the doctor's queue (MDV-P1-103): "Token A-03 · 2 ahead". */}
        {card.token && <FrontDeskTag kind="token" text={[t('board.token', { token: card.token }), card.queuePlace != null && aheadWords(i18n, card.queuePlace - 1)].filter(Boolean).join(' · ')} />}
      </div>
      {(state === 'CheckedIn' || state === 'ReadyForDoctor') && <FrontDeskTag kind="waiting" text={t('board.waiting', { minutes: minutesSince(card.since, now) })} />}
      {card.room && state === 'InConsult' && <FrontDeskTag kind="room" text={t('board.room', { room: card.room })} />}

      {canMove && confirming ? <ConfirmCheckIn {...props} card={card} /> : <NextAction {...props} card={card} className="mt-1 w-full" />}
      {/* shortcut: with billing.invoices off (all of P1) payment is a disabled placeholder; P4 builds payments behind that flag. */}
      {canMove && state === 'Billing' && (paymentAction ?? (
        <>
          <Button variant="outline" className="whitespace-normal" disabled aria-describedby={`${nameId}-pay`}>{t('board.action.paymentReceived')}</Button>
          <p id={`${nameId}-pay`} className="text-xs text-muted-foreground">{t('board.paymentLater')}</p>
        </>
      ))}
      {consultAction?.(card, card.id === primary)}
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

/** Check-in's identity check, on the card and in the list: name and date of birth before "Confirm arrival" (MDV-P1-028). */
function ConfirmCheckIn({ card, online, pendingId, onConfirmCheckIn, onCancelCheckIn }: Props & { card: BoardCard }) {
  const { t, formatCalendarDate } = useI18n();
  const dob = card.dateOfBirth ? formatCalendarDate(card.dateOfBirth) : t('board.dobUnknown');
  return (
    <div role="group" aria-label={t('board.confirm.title')} className="mt-1 flex flex-col gap-2 rounded-md border border-action-600 p-2">
      <p className="text-xs font-bold">{t('board.confirm.title')}</p>
      <p className="text-sm">{t('board.confirm.identity', { name: card.patientName || t('board.unknownPatient'), dob })}</p>
      {card.registrationIncomplete && <p className="text-xs text-warning-700">{t('board.confirm.incomplete')}</p>}
      <Button autoFocus className="whitespace-normal" loading={pendingId === card.id} disabled={!online} onClick={onConfirmCheckIn}>{t('board.confirm.arrived')}</Button>
      <Button variant="outline" onClick={onCancelCheckIn}>{t('booking.cancel')}</Button>
    </div>
  );
}

/** The one action that moves a visit on, the same on the card and in the list; null when the desk has none. */
function NextAction({ card, steps, canMove, online, pendingId, nurseStep, onStartCheckIn, onMove, className }: Props & { card: BoardCard; className?: string }) {
  const { t } = useI18n();
  const state = card.state as BoardColumn;
  const next = nextState(state, steps);
  if (nurseStep && state === 'WithNurse') return nurseStep(card);
  // A consultation is open: the doctor's End moves the card, so the desk has words here and no action (MDV-P1-100).
  if (card.withDoctor && state === 'InConsult') return <FrontDeskTag kind="room" text={t('board.withDoctor')} />;
  if (!canMove || !next || state === 'Done') return null;
  return (
    <Button variant="outline" className={className} loading={pendingId === card.id} disabled={!online}
      onClick={() => (state === 'Booked' ? onStartCheckIn(card.id) : onMove(card.id, next))}>
      {t(state === 'CheckedIn' && next === 'ReadyForDoctor' ? 'board.action.toDoctor' : actionKeys[state])}
    </Button>
  );
}
