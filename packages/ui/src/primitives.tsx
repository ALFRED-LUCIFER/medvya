import type { CSSProperties, ReactNode } from 'react';
import {
  Send, Pipette, Inbox, Eye, Phone, MessageSquareCheck, CircleCheck,
  Sparkles, BadgeCheck, User, ArrowLeftRight, Upload, Lock,
  CircleAlert, TriangleAlert, Info, Pause, CircleMinus, Pill as PillIcon, CircleHelp,
  Clock, Ban, SearchX, WifiOff, Hourglass, CalendarCheck, Tag as TagIcon, UserCheck, DoorOpen, CalendarClock, CalendarX, CircleX, Radio, RefreshCw, Footprints, Ticket, MessageSquareWarning, type LucideIcon,
} from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button } from '#components/ui/button';
import { Chip } from '#components/ui/chip';
import { Tag } from '#components/ui/tag';

// Every clinical state is icon + words + colour, never colour alone (Foundations §01).
// Values come from Handoff §1 and Foundations §04; see docs/02-design.
// Colours are tone tokens (packages/tokens, MDV-P1-080), or a named palette colour where a look differs from its tone's pair today.
// They stay in these tables (applied inline) so the contrast test checks exactly what renders.
const okOnTint = 'var(--color-success-text)';
export type Look = { label: MessageKey; icon: LucideIcon; bg: string; fg: string; border?: string };

export const statusLooks = {
  ordered:   { label: 'status.ordered', icon: Send, bg: 'var(--color-info-bg)', fg: 'var(--color-info-text)' },
  collected: { label: 'status.collected', icon: Pipette, bg: 'var(--color-info-bg)', fg: 'var(--color-info-text)' },
  received:  { label: 'status.received', icon: Inbox, bg: 'var(--color-warning-bg)', fg: 'var(--color-warning-text)' },
  reviewed:  { label: 'status.reviewed', icon: Eye, bg: 'var(--color-brand-bg)', fg: 'var(--color-brand-text)' },
  contacted: { label: 'status.contacted', icon: Phone, bg: 'var(--color-sage-50)', fg: 'var(--color-sage-700)' },
  delivered: { label: 'status.delivered', icon: MessageSquareCheck, bg: 'var(--color-neutral-bg)', fg: 'var(--color-ink-600)' },
  completed: { label: 'status.completed', icon: CircleCheck, bg: 'var(--color-success-700)', fg: 'var(--color-white)' },
} satisfies Record<string, Look>;

export const provenanceLooks = {
  ai:       { label: 'ai.badge', icon: Sparkles, bg: 'var(--color-ai-bg)', fg: 'var(--color-ai-text)', border: '1px dashed var(--color-ai-300)' },
  verified: { label: 'provenance.verified', icon: BadgeCheck, bg: 'var(--color-success-bg)', fg: okOnTint },
  patient:  { label: 'provenance.patient', icon: User, bg: 'var(--color-neutral-bg)', fg: 'var(--color-neutral-text)' },
  his:      { label: 'provenance.his', icon: ArrowLeftRight, bg: 'var(--color-info-bg)', fg: 'var(--color-info-text)' },
  imported: { label: 'provenance.imported', icon: Upload, bg: 'var(--color-ink-100)', fg: 'var(--color-neutral-text)' },
  signed:   { label: 'provenance.signed', icon: Lock, bg: 'var(--color-brand-950)', fg: 'var(--color-white)' },
} satisfies Record<string, Look>;

export const alertLooks = {
  critical:  { label: 'alert.critical', icon: CircleAlert, bg: 'var(--color-danger-bg)', fg: 'var(--color-danger-text)', border: '1px solid var(--color-danger-border)' },
  attention: { label: 'alert.attention', icon: TriangleAlert, bg: 'var(--color-warning-bg)', fg: 'var(--color-warning-text)', border: '1px solid var(--color-warning-border)' },
  info:      { label: 'alert.info', icon: Info, bg: 'var(--color-surface)', fg: 'var(--color-neutral-text)', border: '1px solid var(--color-line)' },
} satisfies Record<string, Look>;

export const recordingLooks = {
  recording: { label: 'recording.rec', icon: Pause, bg: 'var(--color-danger-700)', fg: 'var(--color-white)' }, // icon unused: the blinking dot is the shape cue
  paused:    { label: 'recording.paused', icon: Pause, bg: 'var(--color-warning-bg)', fg: 'var(--color-warning-text)' },
  declined:  { label: 'recording.declined', icon: CircleMinus, bg: 'var(--color-ink-100)', fg: 'var(--color-neutral-text)' },
  given:     { label: 'recording.given', icon: CircleCheck, bg: 'var(--color-success-bg)', fg: okOnTint },
  notAsked:  { label: 'recording.notAsked', icon: CircleHelp, bg: 'var(--color-warning-bg)', fg: 'var(--color-warning-text)' },
} satisfies Record<string, Look>;

// Schedule (P-04): time and appointment states. Hatching (unavailable) or a dashed border is the shape cue beside icon and words.
export const scheduleLooks = {
  // Rendered as a hatch of the muted and line tokens; bg is the darker stop, so the contrast test checks the worst case.
  unavailable: { label: 'schedule.state.unavailable', icon: Ban, bg: 'var(--color-line)', fg: 'var(--color-ink-600)' },
  held: { label: 'schedule.state.held', icon: Hourglass, bg: 'var(--color-warning-bg)', fg: 'var(--color-warning-text)' },
  booked: { label: 'schedule.state.Booked', icon: CalendarCheck, bg: 'var(--color-white)', fg: 'var(--color-info-text)', border: '1px solid var(--color-info-border)' },
  checkedIn: { label: 'schedule.state.CheckedIn', icon: UserCheck, bg: 'var(--color-info-bg)', fg: 'var(--color-info-text)', border: '1px solid var(--color-info-300)' },
  inRoom: { label: 'schedule.state.InConsult', icon: DoorOpen, bg: 'var(--color-brand-bg)', fg: 'var(--color-brand-text)', border: '1px solid var(--color-brand-border)' },
  completed: { label: 'schedule.state.Done', icon: CircleCheck, bg: 'var(--color-neutral-bg)', fg: 'var(--color-ink-600)', border: '1px solid var(--color-ink-200)' },
  noShow: { label: 'schedule.state.NoShow', icon: CalendarX, bg: 'var(--color-danger-25)', fg: 'var(--color-danger-text)', border: '1px dashed var(--color-danger-border)' },
  cancelled: { label: 'schedule.state.Cancelled', icon: CircleX, bg: 'var(--color-surface)', fg: 'var(--color-ink-600)', border: '1px dashed var(--color-ink-400)' },
  needsRebooking: { label: 'schedule.state.NeedsRebooking', icon: CalendarClock, bg: 'var(--color-warning-bg)', fg: 'var(--color-warning-text)', border: '1px dashed var(--color-warning-border)' },
} satisfies Record<string, Look>;

// Reception board (P-01): nonclinical tags on visit cards. The reason is only ever its category.
export const frontDeskLooks = {
  reason: { label: 'board.tag.reason', icon: TagIcon, bg: 'var(--color-neutral-bg)', fg: 'var(--color-neutral-text)' },
  incomplete: { label: 'board.tag.incomplete', icon: TriangleAlert, bg: 'var(--color-warning-bg)', fg: 'var(--color-warning-text)' },
  messageFailed: { label: 'board.tag.messageFailed', icon: MessageSquareWarning, bg: 'var(--color-danger-25)', fg: 'var(--color-danger-text)', border: '1px solid var(--color-danger-border)' }, // a message to the patient failed (MDV-P1-034)
  walkIn: { label: 'board.tag.walkIn', icon: Footprints, bg: 'var(--color-info-bg)', fg: 'var(--color-info-text)' }, // "Walk-in" (MDV-P1-025)
  waiting: { label: 'board.waiting', icon: Hourglass, bg: 'var(--color-white)', fg: 'var(--color-warning-text)' },
  room: { label: 'board.room', icon: DoorOpen, bg: 'var(--color-brand-bg)', fg: 'var(--color-brand-text)' },
  token: { label: 'board.token', icon: Ticket, bg: 'var(--color-info-bg)', fg: 'var(--color-info-text)' },
  live: { label: 'board.live.live', icon: Radio, bg: 'var(--color-success-bg)', fg: okOnTint },
  connecting: { label: 'board.live.connecting', icon: RefreshCw, bg: 'var(--color-neutral-bg)', fg: 'var(--color-neutral-text)' },
  paused: { label: 'board.live.paused', icon: WifiOff, bg: 'var(--color-warning-bg)', fg: 'var(--color-warning-text)' },
} satisfies Record<string, Look>;

const paint = (look: Look): CSSProperties => ({ backgroundColor: look.bg, color: look.fg, border: look.border });

function Pill({ look, text, children }: { look: Look; text?: string; children?: ReactNode }) {
  const { t } = useI18n();
  const Icon = look.icon;
  return (
    <span style={paint(look)} className="inline-flex h-7 items-center gap-1.5 whitespace-nowrap rounded-full px-2.5 text-[13px] font-bold leading-none">
      {children ?? <Icon className="size-4" aria-hidden />}
      {text ?? t(look.label)}
    </span>
  );
}

/** A reception-board tag: icon and words (`text` when they carry a value, e.g. the reason category). Wraps in narrow cards. */
export function FrontDeskTag({ kind, text }: { kind: keyof typeof frontDeskLooks; text?: string }) {
  const { t } = useI18n();
  const look = frontDeskLooks[kind];
  return <Tag tone="neutral" size="sm" label={text ?? t(look.label)} icon={look.icon} style={paint(look)}
    className="items-start border-0 py-1 text-xs font-bold leading-tight [&>svg]:mt-px" />;
}

// Thin wrappers over Chip and Tag (MDV-P1-083). The look tables and these classes keep today's looks exactly until the features move to tones.
export function StatusChip({ state }: { state: keyof typeof statusLooks }) {
  const { t } = useI18n();
  const look = statusLooks[state];
  return <Chip tone="neutral" label={t(look.label)} icon={look.icon} style={paint(look)} className="h-7 min-h-0 border-0 text-[13px] font-bold leading-none" />;
}

export function ProvenanceTag({ source }: { source: keyof typeof provenanceLooks }) {
  const { t } = useI18n();
  const look = provenanceLooks[source];
  const kept = { icon: look.icon, style: paint(look), className: 'h-[26px] max-w-none border-0 px-2.5 py-0 text-[13px] font-bold leading-none whitespace-nowrap' };
  return source === 'ai' ? <Tag tone="ai" {...kept} /> : <Tag tone="neutral" label={t(look.label)} {...kept} />;
}

export function ClinicalAlert({ severity, children }: { severity: keyof typeof alertLooks; children: ReactNode }) {
  const { t } = useI18n();
  const look = alertLooks[severity], Icon = look.icon;
  return (
    <div role={severity === 'critical' ? 'alert' : 'status'} style={paint(look)} className="flex items-start gap-2 rounded-md p-3">
      <Icon className="mt-0.5 size-5 shrink-0" aria-hidden />
      <div className="text-sm">
        <strong className="font-extrabold">{t(look.label)}</strong>
        <p>{children}</p>
      </div>
    </div>
  );
}

export function RecordingPill({ state, elapsedSeconds = 0 }: { state: keyof typeof recordingLooks; elapsedSeconds?: number }) {
  const { t } = useI18n();
  const look = recordingLooks[state];
  if (state !== 'recording') return <Pill look={look} />;
  const mm = String(Math.floor(elapsedSeconds / 60)).padStart(2, '0'), ss = String(elapsedSeconds % 60).padStart(2, '0');
  return (
    <Pill look={look} text={t('recording.rec', { time: `${mm}:${ss}` })}>
      {/* animate-pulse stops under prefers-reduced-motion (styles.css) */}
      <span aria-hidden className="size-2 animate-pulse rounded-full bg-white" />
    </Pill>
  );
}

export type Allergy = { name: string; status: 'verified' | 'patientReported' | 'unknown' };
export type BannerPatient = {
  name: string; age: number; sex: 'F' | 'M' | 'X'; mrn: string; dob: string;
  /** null = allergies not recorded; [] = no known allergies (explicitly confirmed). Never conflate the two. */
  allergies: Allergy[] | null;
  consent?: keyof typeof recordingLooks; encounter?: string; synthetic?: boolean;
};

// Unicode first-strong isolate + non-breaking spaces: keeps Latin IDs and dates intact (order and line) inside Arabic sentences.
const iso = (v: string) => `\u2068${v.replaceAll(' ', '\u00A0')}\u2069`;

const allergyLook: Look = { label: 'banner.allergy', icon: PillIcon, bg: 'var(--color-danger-bg)', fg: 'var(--color-danger-text)' };
const noAllergyLook: Look = { label: 'banner.noAllergies', icon: PillIcon, bg: 'var(--color-neutral-bg)', fg: 'var(--color-neutral-text)' };
const unknownAllergyLook: Look = { label: 'banner.allergiesNotRecorded', icon: CircleHelp, bg: 'var(--color-warning-bg)', fg: 'var(--color-warning-text)' };

/** Fixed patient identity banner for every clinical screen (Foundations §04). Allergies never collapse. */
export function IdentityBanner({ patient }: { patient: BannerPatient }) {
  const { t } = useI18n();
  const initials = patient.name.split(' ').map(p => p[0]).slice(0, 2).join('');
  return (
    <section aria-label={t('banner.patient', { name: patient.name })}
      className="flex flex-wrap items-center gap-x-4 gap-y-2 rounded-lg border bg-card p-3 shadow-[inset_4px_0_0_var(--color-brand-500)] rtl:shadow-[inset_-4px_0_0_var(--color-brand-500)]">
      <div aria-hidden className="grid size-10 place-items-center rounded-full bg-brand-950 font-extrabold text-white">{initials}</div>
      <div>
        <h2 className="text-[17px] font-extrabold">
          {patient.name} <span className="font-medium text-muted-foreground">{patient.age} {patient.sex}</span>
        </h2>
        <p className="font-mono text-xs text-muted-foreground">{t('banner.mrnDob', { mrn: iso(patient.mrn), dob: iso(patient.dob) })}</p>
      </div>
      {patient.allergies === null ? <Pill look={unknownAllergyLook} />
        : patient.allergies.length === 0 ? <Pill look={noAllergyLook} />
        : patient.allergies.map(a => <Pill key={a.name} look={allergyLook}
            text={t('banner.allergy', { name: iso(a.name), status: t(`banner.allergy.${a.status}`) })} />)}
      {patient.consent && <RecordingPill state={patient.consent} />}
      {patient.encounter && <p className="text-[13px] text-muted-foreground">{patient.encounter}</p>}
      {patient.synthetic && <span className="rounded-sm bg-warning-bg px-2 py-1 font-mono text-[10px] font-semibold text-warning-700">{t('banner.synthetic')}</span>}
    </section>
  );
}

export const systemStates = {
  sessionExpired: { title: 'state.sessionExpired.title', body: 'state.sessionExpired.body', cta: 'state.sessionExpired.cta', icon: Clock, color: 'var(--color-neutral-text)' },
  noAccess:       { title: 'err.403', body: 'state.noAccess.body', cta: 'state.noAccess.cta', icon: Ban, color: 'var(--color-danger-text)' },
  removed:        { title: 'err.403', body: 'state.removed.body', cta: 'session.signOut', icon: Ban, color: 'var(--color-danger-text)' },
  notFound:       { title: 'state.notFound.title', body: 'state.notFound.body', cta: 'state.notFound.cta', icon: SearchX, color: 'var(--color-neutral-text)' },
  offline:        { title: 'state.offline.title', body: 'state.offline.body', cta: 'state.offline.cta', icon: WifiOff, color: 'var(--color-warning-700)' },
} satisfies Record<string, { title: MessageKey; body: MessageKey; cta: MessageKey; icon: LucideIcon; color: string }>;

/** F-05 system states: one message, one action, announced to assistive technology. */
export function SystemState({ state, onAction, facility = '' }: { state: keyof typeof systemStates; onAction: () => void; facility?: string }) {
  const { t } = useI18n();
  const s = systemStates[state], Icon = s.icon;
  return (
    <div role={state === 'offline' ? 'status' : 'alert'} className="flex max-w-[420px] flex-col items-start gap-2.5 rounded-xl border bg-card p-6">
      <Icon className="size-7" style={{ color: s.color }} aria-hidden />
      <h1 className="text-[17px] font-extrabold">{t(s.title)}</h1>
      <p className="text-sm text-muted-foreground">{t(s.body, { facility })}</p>
      <Button variant="default" onClick={onAction} className="mt-2">{t(s.cta)}</Button>
    </div>
  );
}
