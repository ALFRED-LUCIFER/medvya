import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { CalendarCheck, FilePen, Siren, CircleAlert, CircleCheck, CircleHelp, ClipboardCheck, DoorOpen, ReceiptText, Syringe, UserCheck, type LucideIcon } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, FrontDeskTag } from '@medvya/ui';
import type { BoardCard, BoardColumn, LiveStatus } from '../../front-desk/domain/board';
import { canConsult } from '../../encounters/domain/encounter';
import type { AllergyState } from '../../patient360/domain/chart';
import { ageOn, type PendingNote, type Scope } from '../domain/today';

// A status is always an icon and words, never colour alone.
const stateIcons: Record<string, LucideIcon> = {
  Booked: CalendarCheck, CheckedIn: UserCheck, WithNurse: Syringe, ReadyForDoctor: ClipboardCheck, InConsult: DoorOpen, Billing: ReceiptText, Done: CircleCheck,
};

type Props = {
  day: string | null; timeZone: string | null; visits: BoardCard[]; next: BoardCard | null; scope: Scope; onScope: (s: Scope) => void;
  /** Whole minutes since a visit entered its state, on the server's clock. */
  waitMinutes: (since: string) => number;
  /** Allergy state by patient, only for patients the clinician may treat; no entry shows no flag. */
  allergyFlags: Record<string, AllergyState>;
  /** A nurse's urgent reason by visit (appointment id), only for patients the clinician may treat. */
  escalations: Record<string, string>; escalationsFailed: boolean;
  /** Patient 360 of a patient. */
  chartPath: (patientId: string) => string;
  loading: boolean; offline: boolean; online: boolean; live: LiveStatus; forbidden: boolean; failed: boolean; onRetry: () => void;
  /** The doctor's unsigned notes from any day, oldest first; each opens its consultation. */
  pendingNotes: PendingNote[]; pendingFailed: boolean;
  /** The server capped the list: there are more unsigned notes than shown. */
  pendingMore: boolean;
  consultPath: (encounterId: string) => string;
  /** "Start consultation" for a visit (MDV-P2-001): the same control as the board, which also asks for access when there is no care relationship. */
  consultAction: (card: BoardCard) => ReactNode;
};

/** D-01 Doctor Today: the next patient, then the day's visits, mine or the clinic's. Names and times only; no clinical content. */
export default function DoctorTodayPage(p: Props) {
  const { t, formatDay } = useI18n();
  return (
    <div className="grid grid-cols-[minmax(0,1fr)] gap-4">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="text-[13px] font-bold text-action-600">{t('nav.today')}</p>
          <h1 className="text-[26px] font-extrabold tracking-tight">{p.day ? t('today.title', { day: formatDay(p.day) }) : t('nav.today')}</h1>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {!p.forbidden && <p role="status" className="inline-flex min-h-9 items-center"><FrontDeskTag kind={p.live} /></p>}
          <div role="group" aria-label={t('today.scope')} className="flex gap-1">
            {(['mine', 'clinic'] as const).map(s => <ScopeButton key={s} scope={s} active={p.scope === s} onScope={p.onScope} />)}
          </div>
        </div>
      </header>
      {p.escalationsFailed && <p role="alert" className="text-sm font-semibold text-danger-700">{t('today.escalation.error')}</p>}
      <Body {...p} />
      <PendingNotes {...p} />
    </div>
  );
}

function ScopeButton({ scope, active, onScope }: { scope: Scope; active: boolean; onScope: (s: Scope) => void }) {
  const { t } = useI18n();
  return <Button variant={active ? 'navy' : 'outline'} className="min-h-11" aria-pressed={active} onClick={() => onScope(scope)}>{t(`today.scope.${scope}`)}</Button>;
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

function Body(p: Props) {
  const { t } = useI18n();
  if (p.forbidden) return <Message title={t('err.403')} body={t('board.forbidden.body')} />;
  if (p.offline) return <Message title={t('state.offline.title')} body={t('board.offline')} />;
  if (p.failed) return <Message title={t('board.error.title')} body={t('board.error.body')} action={{ label: t('schedule.retry'), run: p.onRetry }} />;
  if (p.loading || !p.day) return <Message busy title={t('board.loading')} />;
  if (p.visits.length === 0) return <Message title={t('today.empty.title')} body={t(p.scope === 'mine' ? 'today.empty.mine' : 'today.empty.clinic')} />;
  return (
    <>
      {p.next && <Hero {...p} card={p.next} />}
      <section aria-labelledby="today-list" className="flex flex-col gap-2">
        <h2 id="today-list" className="text-[15px] font-extrabold">{t('today.list')}</h2>
        <ul className="flex flex-col gap-2">{p.visits.map(v => <li key={v.id}><Visit {...p} card={v} /></li>)}</ul>
      </section>
    </>
  );
}

function Hero(p: Props & { card: BoardCard }) {
  const { t, formatTime } = useI18n();
  const c = p.card;
  return (
    <section aria-labelledby="today-next" className="flex flex-col gap-3 rounded-xl border-2 border-action-600 bg-card p-4">
      <h2 id="today-next" className="text-[13px] font-bold text-action-600">{t(c.state === 'InConsult' ? 'today.hero.current' : 'today.hero.next')}</h2>
      <div>
        <p className="text-xl font-extrabold"><PatientLink {...p} card={c} /></p>
        <p className="text-sm text-muted-foreground">{formatTime(new Date(c.start), p.timeZone ?? undefined)}{' · '}{t(`reasonCategory.${c.reasonCategory}`)}</p>
      </div>
      <Chips {...p} card={c} />
      {/* Full width and the first thing on a phone, so the thumb reaches it. */}
      <div className="[&_button]:w-full [&_button]:min-h-12">{canConsult(c.state) ? p.consultAction(c) : null}</div>
    </section>
  );
}

function Chips(p: Props & { card: BoardCard }) {
  const { t } = useI18n();
  const c = p.card, Icon = stateIcons[c.state] ?? CalendarCheck;
  return (
    <div className="flex flex-wrap gap-1.5">
      <span className="inline-flex items-center gap-1.5 rounded-full bg-muted px-2.5 py-1 text-[13px] font-bold">
        <Icon aria-hidden className="size-4" />{t(`board.column.${c.state as BoardColumn}`)}{c.state === 'CheckedIn' && ` · ${t('board.waiting', { minutes: p.waitMinutes(c.since) })}`}
      </span>
      {p.escalations[c.id] && (
        <span className="inline-flex items-center gap-1.5 rounded-full border-2 border-danger-700 px-2.5 py-1 text-danger-700 text-[13px] font-bold">
          <Siren aria-hidden className="size-4" />{t('today.escalation', { reason: p.escalations[c.id]! })}
        </span>
      )}
      <AllergyFlag state={p.allergyFlags[c.patientId]} />
      {c.registrationIncomplete && <FrontDeskTag kind="incomplete" />}
      {c.walkIn && <FrontDeskTag kind="walkIn" />}
    </div>
  );
}

function Visit(p: Props & { card: BoardCard }) {
  const { t, formatTime } = useI18n();
  const c = p.card, age = ageOn(c.dateOfBirth, p.day ?? '');
  const nameId = `today-visit-${c.id}`;
  return (
    <article aria-labelledby={nameId} className="flex flex-wrap items-center justify-between gap-3 rounded-lg border bg-card px-3 py-2.5">
      <div className="flex min-w-0 flex-col gap-1.5">
        <h3 id={nameId} className="text-sm font-bold">
          <span className="font-mono text-xs font-semibold text-muted-foreground">{formatTime(new Date(c.start), p.timeZone ?? undefined)}</span>{' '}
          <PatientLink {...p} card={c} />{age !== null && <span className="font-normal text-muted-foreground">{' · '}{t('today.age', { years: age })}</span>}
        </h3>
        <p className="text-xs text-muted-foreground">{[t(`reasonCategory.${c.reasonCategory}`), p.scope === 'clinic' ? c.calendarName : ''].filter(Boolean).join(' · ')}</p>
        <Chips {...p} card={c} />
      </div>
      {canConsult(c.state) && <div className="[&_button]:min-w-40">{p.consultAction(c)}</div>}
    </article>
  );
}

/** The patient's name opens Patient 360; the server still checks the care relationship when it opens. */
function PatientLink(p: Props & { card: BoardCard }) {
  const { t } = useI18n();
  const c = p.card, name = c.patientName || t('board.unknownPatient');
  return c.patientId ? <Link to={p.chartPath(c.patientId)} className="underline-offset-2 hover:underline focus-visible:underline">{name}</Link> : <>{name}</>;
}

// An allergy state is an icon and words, never colour alone; "not recorded" is never worded as "none".
const flagLooks = {
  Listed: { Icon: CircleAlert, key: 'today.allergy.listed' },
  NoKnownAllergies: { Icon: CircleCheck, key: 'banner.noAllergies' },
  NotRecorded: { Icon: CircleHelp, key: 'banner.allergiesNotRecorded' },
} as const satisfies Record<AllergyState, { Icon: LucideIcon; key: string }>;

function AllergyFlag({ state }: { state: AllergyState | undefined }) {
  const { t } = useI18n();
  if (!state) return null;
  const { Icon, key } = flagLooks[state];
  return <span className="inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[13px] font-bold"><Icon aria-hidden className="size-4" />{t(key)}</span>;
}

/** Unsigned notes from today and earlier days (MDV-P2-002), so none is forgotten; a note leaves the list only when it is signed. */
function PendingNotes(p: Props) {
  const { t, tCount, formatDate } = useI18n();
  if (p.pendingFailed) return <p role="alert" className="text-sm text-muted-foreground">{t('today.pending.error')}</p>;
  if (p.pendingNotes.length === 0) return null;
  return (
    <section aria-labelledby="today-pending" className="flex flex-col gap-2">
      <h2 id="today-pending" className="text-[15px] font-extrabold">{p.pendingMore ? t('today.pending.more', { count: p.pendingNotes.length }) : tCount('today.pending', p.pendingNotes.length)}</h2>
      <ul className="flex flex-col gap-2">
        {p.pendingNotes.map(n => (
          <li key={n.encounterId} className="flex flex-wrap items-center justify-between gap-3 rounded-lg border bg-card px-3 py-2.5">
            <span className="flex items-center gap-2 text-sm">
              <FilePen aria-hidden className="size-4 shrink-0" />
              <span className="font-bold">{n.patientName || t('board.unknownPatient')}</span>
              <span className="text-muted-foreground">{formatDate(new Date(n.startedAt), p.timeZone ?? undefined)}{' · '}{t(n.drafted ? 'today.pending.draft' : 'today.pending.empty')}</span>
            </span>
            <Link to={p.consultPath(n.encounterId)} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('today.pending.open')}</Link>
          </li>
        ))}
      </ul>
    </section>
  );
}
