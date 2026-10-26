import type { CSSProperties, ReactNode } from 'react';
import {
  Send, Pipette, Inbox, Eye, Phone, MessageSquareCheck, CircleCheck,
  Sparkles, BadgeCheck, User, ArrowLeftRight, Upload, Lock,
  CircleAlert, TriangleAlert, Info, Pause, CircleMinus, Pill as PillIcon, CircleHelp,
  Clock, Ban, SearchX, WifiOff, Hourglass, CalendarCheck, Tag, UserCheck, DoorOpen, CalendarX, CircleX, Radio, RefreshCw, Footprints, type LucideIcon,
} from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button } from '#components/ui/button';

// Every clinical state is icon + words + colour, never colour alone (Foundations §01).
// Values come from Handoff §1 and Foundations §04; see docs/02-design.
// Deviation: success text on the #E6F4EF tint is #07774F, not #087F5B (4.42:1 fails WCAG AA; 4.93:1 passes).
// Colours stay in these tables (applied inline) so the contrast test checks exactly what renders.
const okOnTint = '#07774F';
export type Look = { label: MessageKey; icon: LucideIcon; bg: string; fg: string; border?: string };

export const statusLooks = {
  ordered:   { label: 'status.ordered', icon: Send, bg: '#EFF4FF', fg: '#1D4ED8' },
  collected: { label: 'status.collected', icon: Pipette, bg: '#EFF4FF', fg: '#1D4ED8' },
  received:  { label: 'status.received', icon: Inbox, bg: '#FDF5E7', fg: '#7A4710' },
  reviewed:  { label: 'status.reviewed', icon: Eye, bg: '#E7F6F4', fg: '#0F766E' },
  contacted: { label: 'status.contacted', icon: Phone, bg: '#EEF4EF', fg: '#3E6A4D' },
  delivered: { label: 'status.delivered', icon: MessageSquareCheck, bg: '#F1F5F8', fg: '#506579' },
  completed: { label: 'status.completed', icon: CircleCheck, bg: '#087F5B', fg: '#FFFFFF' },
} satisfies Record<string, Look>;

export const provenanceLooks = {
  ai:       { label: 'ai.badge', icon: Sparkles, bg: '#F2EFFC', fg: '#5B3FC4', border: '1px dashed #A897E8' },
  verified: { label: 'provenance.verified', icon: BadgeCheck, bg: '#E6F4EF', fg: okOnTint },
  patient:  { label: 'provenance.patient', icon: User, bg: '#F1F5F8', fg: '#334E68' },
  his:      { label: 'provenance.his', icon: ArrowLeftRight, bg: '#EFF4FF', fg: '#1D4ED8' },
  imported: { label: 'provenance.imported', icon: Upload, bg: '#EDF1F5', fg: '#334E68' },
  signed:   { label: 'provenance.signed', icon: Lock, bg: '#102A43', fg: '#FFFFFF' },
} satisfies Record<string, Look>;

export const alertLooks = {
  critical:  { label: 'alert.critical', icon: CircleAlert, bg: '#FDECEE', fg: '#8A1626', border: '1px solid #F3B9C0' },
  attention: { label: 'alert.attention', icon: TriangleAlert, bg: '#FDF5E7', fg: '#7A4710', border: '1px solid #EFD5A6' },
  info:      { label: 'alert.info', icon: Info, bg: '#F7FAFC', fg: '#334E68', border: '1px solid #DFE7ED' },
} satisfies Record<string, Look>;

export const recordingLooks = {
  recording: { label: 'recording.rec', icon: Pause, bg: '#B42333', fg: '#FFFFFF' }, // icon unused: the blinking dot is the shape cue
  paused:    { label: 'recording.paused', icon: Pause, bg: '#FDF5E7', fg: '#7A4710' },
  declined:  { label: 'recording.declined', icon: CircleMinus, bg: '#EDF1F5', fg: '#334E68' },
  given:     { label: 'recording.given', icon: CircleCheck, bg: '#E6F4EF', fg: okOnTint },
  notAsked:  { label: 'recording.notAsked', icon: CircleHelp, bg: '#FDF5E7', fg: '#7A4710' },
} satisfies Record<string, Look>;

// Schedule (P-04): time and appointment states. Hatching (unavailable) or a dashed border is the shape cue beside icon and words.
export const scheduleLooks = {
  // Rendered as a hatch of the muted and line tokens; bg is the darker stop, so the contrast test checks the worst case.
  unavailable: { label: 'schedule.state.unavailable', icon: Ban, bg: 'var(--color-line)', fg: 'var(--color-ink-600)' },
  held: { label: 'schedule.state.held', icon: Hourglass, bg: '#FDF5E7', fg: '#7A4710' },
  booked: { label: 'schedule.state.Booked', icon: CalendarCheck, bg: '#FFFFFF', fg: '#1D4ED8', border: '1px solid #2563EB' },
  checkedIn: { label: 'schedule.state.CheckedIn', icon: UserCheck, bg: '#EFF4FF', fg: '#1D4ED8', border: '1px solid #93B4F5' },
  inRoom: { label: 'schedule.state.InConsult', icon: DoorOpen, bg: '#E7F6F4', fg: '#0F766E', border: '1px solid #0E9384' },
  completed: { label: 'schedule.state.Done', icon: CircleCheck, bg: '#F1F5F8', fg: '#506579', border: '1px solid #CBD6E0' },
  noShow: { label: 'schedule.state.NoShow', icon: CalendarX, bg: '#FFF7F8', fg: '#8A1626', border: '1px dashed #C83A4B' },
  cancelled: { label: 'schedule.state.Cancelled', icon: CircleX, bg: '#F7FAFC', fg: '#506579', border: '1px dashed #8296A8' },
} satisfies Record<string, Look>;

// Reception board (P-01): nonclinical tags on visit cards. The reason is only ever its category.
export const frontDeskLooks = {
  reason: { label: 'board.tag.reason', icon: Tag, bg: '#F1F5F8', fg: '#334E68' },
  incomplete: { label: 'board.tag.incomplete', icon: TriangleAlert, bg: '#FDF5E7', fg: '#7A4710' },
  walkIn: { label: 'board.tag.walkIn', icon: Footprints, bg: '#EFF4FF', fg: '#1D4ED8' }, // "Walk-in" (MDV-P1-025)
  waiting: { label: 'board.waiting', icon: Hourglass, bg: '#FFFFFF', fg: '#7A4710' },
  room: { label: 'board.room', icon: DoorOpen, bg: '#E7F6F4', fg: '#0F766E' },
  live: { label: 'board.live.live', icon: Radio, bg: '#E6F4EF', fg: okOnTint },
  connecting: { label: 'board.live.connecting', icon: RefreshCw, bg: '#F1F5F8', fg: '#334E68' },
  paused: { label: 'board.live.paused', icon: WifiOff, bg: '#FDF5E7', fg: '#7A4710' },
} satisfies Record<string, Look>;

const paint = (look: Look): CSSProperties => ({ backgroundColor: look.bg, color: look.fg, border: look.border });

function Pill({ look, text, tag = false, children }: { look: Look; text?: string; tag?: boolean; children?: ReactNode }) {
  const { t } = useI18n();
  const Icon = look.icon;
  return (
    <span style={paint(look)} className={`inline-flex items-center gap-1.5 px-2.5 text-[13px] font-bold leading-none whitespace-nowrap ${tag ? 'h-[26px] rounded-sm' : 'h-7 rounded-full'}`}>
      {children ?? <Icon className="size-4" aria-hidden />}
      {text ?? t(look.label)}
    </span>
  );
}

/** A reception-board tag: icon and words (`text` when they carry a value, e.g. the reason category). Wraps in narrow cards. */
export function FrontDeskTag({ kind, text }: { kind: keyof typeof frontDeskLooks; text?: string }) {
  const { t } = useI18n();
  const look = frontDeskLooks[kind], Icon = look.icon;
  return (
    <span style={paint(look)} className="inline-flex max-w-full items-start gap-1 rounded-sm px-2 py-1 text-xs font-bold leading-tight">
      <Icon className="mt-px size-3.5 shrink-0" aria-hidden />{text ?? t(look.label)}
    </span>
  );
}

export const StatusChip = ({ state }: { state: keyof typeof statusLooks }) => <Pill look={statusLooks[state]} />;

export const ProvenanceTag = ({ source }: { source: keyof typeof provenanceLooks }) => <Pill look={provenanceLooks[source]} tag />;

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

const allergyLook: Look = { label: 'banner.allergy', icon: PillIcon, bg: '#FDECEE', fg: '#8A1626' };
const noAllergyLook: Look = { label: 'banner.noAllergies', icon: PillIcon, bg: '#F1F5F8', fg: '#334E68' };
const unknownAllergyLook: Look = { label: 'banner.allergiesNotRecorded', icon: CircleHelp, bg: '#FDF5E7', fg: '#7A4710' };

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
      {patient.synthetic && <span className="rounded-sm bg-[#FDF5E7] px-2 py-1 font-mono text-[10px] font-semibold text-warning-700">{t('banner.synthetic')}</span>}
    </section>
  );
}

export const systemStates = {
  sessionExpired: { title: 'state.sessionExpired.title', body: 'state.sessionExpired.body', cta: 'state.sessionExpired.cta', icon: Clock, color: '#334E68' },
  noAccess:       { title: 'err.403', body: 'state.noAccess.body', cta: 'state.noAccess.cta', icon: Ban, color: '#8A1626' },
  notFound:       { title: 'state.notFound.title', body: 'state.notFound.body', cta: 'state.notFound.cta', icon: SearchX, color: '#334E68' },
  offline:        { title: 'state.offline.title', body: 'state.offline.body', cta: 'state.offline.cta', icon: WifiOff, color: '#975A16' },
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
      <Button variant="navy" onClick={onAction} className="mt-2">{t(s.cta)}</Button>
    </div>
  );
}
