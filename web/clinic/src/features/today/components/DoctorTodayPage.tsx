import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { Activity, CalendarCheck, FilePen, Pill, Siren, CircleAlert, CircleCheck, CircleHelp, ClipboardCheck, DoorOpen, ReceiptText, Syringe, UserCheck, type LucideIcon } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Card, Chip, cn, FrontDeskTag, PageContainer, PageHeader, PageMessage, SegmentedControl } from '@medvya/ui';
import { aheadWords, type BoardCard, type BoardColumn, type LiveStatus } from '../../front-desk/domain/board';
import { canConsult } from '../../encounters/domain/encounter';
import type { AllergyState } from '../../patient360/domain/chart';
import { authoriseTarget } from '../../prescription/domain/authorisation';
import { ageOn, type PendingNote, type PendingRx, type Scope } from '../domain/today';

// A status is always an icon and words, never colour alone.
const stateIcons: Record<string, LucideIcon> = {
  Booked: CalendarCheck, CheckedIn: UserCheck, WithNurse: Syringe, ReadyForDoctor: ClipboardCheck, InConsult: DoorOpen, Billing: ReceiptText, Done: CircleCheck,
};

type Props = {
  day: string | null; timeZone: string | null; visits: BoardCard[]; next: BoardCard | null; scope: Scope; onScope: (s: Scope) => void;
  /** Whole minutes since a visit entered its state, on the server's clock. */
  waitMinutes: (since: string) => number;
  /** Allergy state by patient, only for patients the clinician may treat; no entry shows no flag. */
  allergyFlags: Record<string, { state: AllergyState; abnormalVitals: boolean }>;
  /** A nurse's urgent reason by visit (appointment id), only for patients the clinician may treat. */
  escalations: Record<string, string>; escalationsFailed: boolean;
  /** The visits with an open escalation, in queue order: pinned above Next patient (MDV-P1-103). */
  escalated: BoardCard[];
  /** Patient 360 of a patient. */
  chartPath: (patientId: string) => string;
  loading: boolean; offline: boolean; online: boolean; live: LiveStatus; forbidden: boolean; failed: boolean; onRetry: () => void;
  /** The doctor's unsigned notes from any day, oldest first; each opens its consultation. */
  pendingNotes: PendingNote[]; pendingFailed: boolean;
  /** The server capped the list: there are more unsigned notes than shown. */
  pendingMore: boolean;
  consultPath: (encounterId: string) => string;
  /** Ended visits whose prescription waits to be authorised (MDV-P1-090), oldest first; each opens the consultation at the prescription. */
  pendingRx: PendingRx[]; pendingRxMore: boolean; pendingRxFailed: boolean;
  /** "Start consultation" for a visit (MDV-P2-001): the same control as the board, which also asks for access when there is no care relationship. */
  /** The visit's Start or Open consultation button; `primary` only on the next-patient card, so one filled button shows (MDV-P2-060). */
  consultAction: (card: BoardCard, primary: boolean) => ReactNode;
};

/** D-01 Doctor Today: the next patient, then the day's visits, mine or the clinic's. Names and times only; no clinical content. */
export default function DoctorTodayPage(p: Props) {
  const { t, formatDay } = useI18n();
  return (
    <PageContainer width="fluid">
      {/* The title is fixed and the day is secondary text that arrives without moving the row (MDV-P2-059 AC2); no eyebrow repeating the menu item. */}
      <PageHeader title={t('nav.today')} meta={p.day && formatDay(p.day)}
        actions={!p.forbidden && <p role="status" className="inline-flex min-h-9 items-center"><FrontDeskTag kind={p.live} /></p>}>
        {/* Under the title, as the board's view switch: the 48 px track would make the title row taller than 44 px (MDV-P2-059). */}
        <SegmentedControl label={t('today.scope')} value={p.scope} onValueChange={p.onScope} options={(['mine', 'clinic'] as const).map(s => ({ value: s, label: t(`today.scope.${s}`) }))} />
      </PageHeader>
      {p.escalationsFailed && <p role="alert" className="text-sm font-semibold text-danger-700">{t('today.escalation.error')}</p>}
      <Body {...p} />
      <PendingNotes {...p} />
      <PendingPrescriptions {...p} />
    </PageContainer>
  );
}

function Body(p: Props) {
  const { t } = useI18n();
  if (p.forbidden) return <PageMessage kind="denied" title={t('err.403')}>{t('board.forbidden.body')}</PageMessage>;
  if (p.offline) return <PageMessage kind="error" title={t('state.offline.title')}>{t('board.offline')}</PageMessage>;
  if (p.failed) return <PageMessage kind="error" title={t('board.error.title')} action={<Button onClick={p.onRetry}>{t('schedule.retry')}</Button>}>{t('board.error.body')}</PageMessage>;
  if (p.loading || !p.day) return <PageMessage kind="busy">{t('board.loading')}</PageMessage>;
  if (p.visits.length === 0) return <PageMessage kind="empty" title={t('today.empty.title')}>{t(p.scope === 'mine' ? 'today.empty.mine' : 'today.empty.clinic')}</PageMessage>;
  return (
    <>
      {p.escalated.length > 0 && <Escalated {...p} />}
      {p.next && <Hero {...p} card={p.next} />}
      <section aria-labelledby="today-list" className="flex flex-col gap-2">
        <h2 id="today-list" className="text-[15px] font-extrabold">{t('today.list')}</h2>
        <ul className="flex flex-col gap-2">{p.visits.map(v => <li key={v.id}><Visit {...p} card={v} /></li>)}</ul>
      </section>
    </>
  );
}

/** A nurse's escalation comes before Next patient (MDV-P1-103): the reason in words with an icon, never colour alone. */
function Escalated(p: Props) {
  const { t } = useI18n();
  return (
    <section aria-labelledby="today-escalated" className="flex flex-col gap-3 rounded-xl border-2 border-danger-border bg-danger-bg p-4">
      <h2 id="today-escalated" className="inline-flex items-center gap-1.5 text-label text-danger-text"><Siren aria-hidden className="size-4" />{t('today.escalated.title')}</h2>
      <ul className="flex flex-col gap-2">
        {p.escalated.map(c => (
          <li key={c.id} className="relative flex flex-wrap items-center justify-between gap-3">
            <div className="flex min-w-0 flex-col gap-1.5">
              <p className="text-subheading"><Token card={c} /><PatientLink {...p} card={c} stretch /></p>
              <Chips {...p} card={c} />
            </div>
            {canConsult(c.state) && <div className="relative [&_button]:min-w-40">{p.consultAction(c, false)}</div>}
          </li>
        ))}
      </ul>
    </section>
  );
}

/** The waiting-room token ("A-07"), the one the queue display calls; none before check-in. Isolated, so in Arabic it stays apart from a Latin name. */
function Token({ card }: { card: BoardCard }) {
  return card.token ? <><bdi className="font-mono text-mono tabular-nums">{card.token}</bdi>{' '}</> : null;
}

function Hero(p: Props & { card: BoardCard }) {
  const { t, formatTime } = useI18n();
  const c = p.card;
  return (
    <section aria-labelledby="today-next" className="relative flex flex-col gap-3 rounded-xl border-2 border-action-600 bg-card p-4">
      <h2 id="today-next" className="text-[13px] font-bold text-action-600">{t(c.state === 'InConsult' ? 'today.hero.current' : 'today.hero.next')}</h2>
      <div>
        <p className="text-xl font-extrabold"><Token card={c} /><PatientLink {...p} card={c} stretch /></p>
        <p className="text-sm text-muted-foreground">{formatTime(new Date(c.start), p.timeZone ?? undefined)}{' · '}{t(`reasonCategory.${c.reasonCategory}`)}</p>
      </div>
      <Chips {...p} card={c} />
      {/* Full width and the first thing on a phone, so the thumb reaches it. */}
      <div className="relative [&_button]:w-full [&_button]:min-h-12">{canConsult(c.state) ? p.consultAction(c, true) : null}</div>
    </section>
  );
}

function Chips(p: Props & { card: BoardCard }) {
  const i18n = useI18n(), { t } = i18n;
  const c = p.card, Icon = stateIcons[c.state] ?? CalendarCheck;
  return (
    <div className="flex flex-wrap gap-1.5">
      <Chip tone="neutral" icon={Icon} label={[t(`board.column.${c.state as BoardColumn}`), c.state === 'CheckedIn' ? t('board.waiting', { minutes: p.waitMinutes(c.since) }) : '',
        c.queuePlace != null ? aheadWords(i18n, c.queuePlace - 1) : ''].filter(Boolean).join(' · ')} className="whitespace-normal" />
      {p.escalations[c.id] && <Chip tone="danger" icon={Siren} label={t('today.escalation', { reason: p.escalations[c.id]! })} className="whitespace-normal" />}
      <AllergyFlag state={p.allergyFlags[c.patientId]?.state} />
      {p.allergyFlags[c.patientId]?.abnormalVitals && <Chip tone="danger" icon={Activity} label={t('today.vitals.abnormal')} />}
      {c.registrationIncomplete && <FrontDeskTag kind="incomplete" />}
      {c.walkIn && <FrontDeskTag kind="walkIn" />}
    </div>
  );
}

function Visit(p: Props & { card: BoardCard }) {
  const { t, formatTime } = useI18n();
  const c = p.card, age = ageOn(c.dateOfBirth, p.day ?? '');
  return (
    <Card size="item" className="relative flex-row flex-wrap items-center justify-between gap-3">
      <div className="flex min-w-0 flex-col gap-1.5">
        <h3 className="text-sm font-bold">
          <span className="font-mono text-xs font-semibold text-muted-foreground">{formatTime(new Date(c.start), p.timeZone ?? undefined)}</span>{' '}
          <Token card={c} /><PatientLink {...p} card={c} stretch />{age !== null && <span className="font-normal text-muted-foreground">{' · '}{t('today.age', { years: age })}</span>}
        </h3>
        <p className="text-xs text-muted-foreground">{[t(`reasonCategory.${c.reasonCategory}`), p.scope === 'clinic' ? c.calendarName : ''].filter(Boolean).join(' · ')}</p>
        <Chips {...p} card={c} />
      </div>
      {canConsult(c.state) && <div className="relative [&_button]:min-w-40">{p.consultAction(c, false)}</div>}
    </Card>
  );
}

/**
 * The patient's name opens Patient 360; the server still checks the care relationship when it opens.
 * With `stretch` the link covers its row, so the whole row (its nearest positioned box) is the target, not the 19 px name (MDV-P1-078); the row's own button sits above it.
 */
function PatientLink(p: Props & { card: BoardCard; stretch?: boolean }) {
  const { t } = useI18n();
  const c = p.card, name = c.patientName || t('board.unknownPatient');
  return c.patientId
    ? <Link to={p.chartPath(c.patientId)} className={cn('underline-offset-2 hover:underline focus-visible:underline', p.stretch && 'after:absolute after:inset-0')}>{name}</Link>
    : <>{name}</>;
}

// An allergy state is an icon and words, never colour alone; "not recorded" is never worded as "none". Tones as AllergyBlock's: recorded danger,
// not recorded warning, none known neutral (MDV-P1-081, MDV-P2-060).
const flagLooks = {
  Listed: { Icon: CircleAlert, key: 'today.allergy.listed', tone: 'danger' },
  NoKnownAllergies: { Icon: CircleCheck, key: 'banner.noAllergies', tone: 'neutral' },
  NotRecorded: { Icon: CircleHelp, key: 'banner.allergiesNotRecorded', tone: 'warning' },
} as const satisfies Record<AllergyState, { Icon: LucideIcon; key: string; tone: 'danger' | 'neutral' | 'warning' }>;

function AllergyFlag({ state }: { state: AllergyState | undefined }) {
  const { t } = useI18n();
  if (!state) return null;
  const { Icon, key, tone } = flagLooks[state];
  return <Chip tone={tone} icon={Icon} label={t(key)} />;
}

/**
 * Unsigned notes from today and earlier days (MDV-P2-002), so none is forgotten; a note leaves the list only when it is signed. Below them,
 * signed consultations still open from an earlier day (MDV audit UX-11), until they are ended.
 */
function PendingNotes(p: Props) {
  const { t, tCount } = useI18n();
  if (p.pendingFailed) return <p role="alert" className="text-sm text-muted-foreground">{t('today.pending.error')}</p>;
  const toSign = p.pendingNotes.filter(n => !n.noteSigned), open = p.pendingNotes.filter(n => n.noteSigned);
  return (
    <>
      {toSign.length > 0 && (
        <section aria-labelledby="today-pending" className="flex flex-col gap-2">
          <h2 id="today-pending" className="text-[15px] font-extrabold">{p.pendingMore ? t('today.pending.more', { count: toSign.length }) : tCount('today.pending', toSign.length)}</h2>
          <ul className="flex flex-col gap-2">{toSign.map(n => <PendingRow key={n.encounterId} note={n} {...p} />)}</ul>
        </section>
      )}
      {open.length > 0 && (
        <section aria-labelledby="today-open" className="flex flex-col gap-2">
          <h2 id="today-open" className="text-[15px] font-extrabold">{t('today.open.title')}</h2>
          <ul className="flex flex-col gap-2">{open.map(n => <PendingRow key={n.encounterId} note={n} {...p} />)}</ul>
        </section>
      )}
    </>
  );
}

/** Prescriptions to authorise (MDV-P1-090): a draft left when the visit ended stays here until it is authorised or discarded. */
function PendingPrescriptions(p: Props) {
  const { t, tCount, formatDate } = useI18n();
  if (p.pendingRxFailed) return <p role="alert" className="text-sm text-muted-foreground">{t('today.rx.error')}</p>;
  if (p.pendingRx.length === 0) return null;
  return (
    <section aria-labelledby="today-rx" className="flex flex-col gap-2">
      <h2 id="today-rx" className="text-subheading">{p.pendingRxMore ? t('today.rx.more', { count: p.pendingRx.length }) : tCount('today.rx', p.pendingRx.length)}</h2>
      <ul className="flex flex-col gap-2">
        {p.pendingRx.map(r => (
          <li key={r.encounterId} className="flex flex-wrap items-center justify-between gap-3 rounded-lg border px-3 py-2.5">
            <span className="flex flex-wrap items-center gap-2 text-sm">
              <Pill aria-hidden className="size-4 shrink-0" />
              <span className="font-bold">{r.patientName || t('board.unknownPatient')}</span>
              <span className="text-muted-foreground">{formatDate(new Date(r.endedAt), p.timeZone ?? undefined)}{' · '}{tCount('today.rx.lines', r.lines)}</span>
            </span>
            <Link to={`${p.consultPath(r.encounterId)}#${authoriseTarget}`} className={buttonVariants({ variant: 'outline' })}
              aria-label={t('today.rx.openFor', { name: r.patientName || t('board.unknownPatient') })}>{t('today.rx.open')}</Link>
          </li>
        ))}
      </ul>
    </section>
  );
}

function PendingRow({ note: n, timeZone, consultPath }: Props & { note: PendingNote }) {
  const { t, formatDate } = useI18n();
  const state = n.noteSigned ? 'today.open.signed' : n.drafted ? 'today.pending.draft' : 'today.pending.empty';
  return (
    <li><Card size="item" className="flex-row flex-wrap items-center justify-between gap-3">
      <span className="flex flex-wrap items-center gap-2 text-sm">
        {n.openPastDay ? <CircleAlert aria-hidden className="size-4 shrink-0" /> : <FilePen aria-hidden className="size-4 shrink-0" />}
        <span className="font-bold">{n.patientName || t('board.unknownPatient')}</span>
        <span className="text-muted-foreground">{formatDate(new Date(n.startedAt), timeZone ?? undefined)}{' · '}{t(state)}</span>
        {n.openPastDay && <span className="font-bold">{t('today.open.pastDay')}</span>}
      </span>
      <Link to={consultPath(n.encounterId)} className={buttonVariants({ variant: 'outline' })}>{t(n.noteSigned ? 'today.open.action' : 'today.pending.open')}</Link>
    </Card></li>
  );
}
