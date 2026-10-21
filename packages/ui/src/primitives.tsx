import type { ReactNode } from 'react';
import { Box, Button, Typography } from '@mui/material';
import { keyframes } from '@mui/material/styles';
import {
  Send, Colorize, Inbox, Visibility, Call, MarkChatRead, TaskAlt,
  AutoAwesome, Verified, Person, SyncAlt, Upload, Lock,
  Error as ErrorIcon, Warning, Info, Pause, DoNotDisturbOn, Medication, HelpOutline,
  LockClock, Block, TravelExplore, WifiOff,
} from '@mui/icons-material';
import { useI18n, type MessageKey } from '@medvya/i18n';

// Every clinical state is icon + words + colour, never colour alone (Foundations §01).
// Values come from Handoff §1 and Foundations §04; see docs/02-design.
// Deviation: success text on the #E6F4EF tint is #07774F, not #087F5B (4.42:1 fails WCAG AA; 4.93:1 passes).
const okOnTint = '#07774F';
export type Look = { label: MessageKey; icon: typeof Send; bg: string; fg: string; border?: string };

export const statusLooks = {
  ordered:   { label: 'status.ordered', icon: Send, bg: '#EFF4FF', fg: '#1D4ED8' },
  collected: { label: 'status.collected', icon: Colorize, bg: '#EFF4FF', fg: '#1D4ED8' },
  received:  { label: 'status.received', icon: Inbox, bg: '#FDF5E7', fg: '#7A4710' },
  reviewed:  { label: 'status.reviewed', icon: Visibility, bg: '#E7F6F4', fg: '#0F766E' },
  contacted: { label: 'status.contacted', icon: Call, bg: '#EEF4EF', fg: '#3E6A4D' },
  delivered: { label: 'status.delivered', icon: MarkChatRead, bg: '#F1F5F8', fg: '#506579' },
  completed: { label: 'status.completed', icon: TaskAlt, bg: '#087F5B', fg: '#FFFFFF' },
} satisfies Record<string, Look>;

export const provenanceLooks = {
  ai:       { label: 'ai.badge', icon: AutoAwesome, bg: '#F2EFFC', fg: '#5B3FC4', border: '1px dashed #A897E8' },
  verified: { label: 'provenance.verified', icon: Verified, bg: '#E6F4EF', fg: okOnTint },
  patient:  { label: 'provenance.patient', icon: Person, bg: '#F1F5F8', fg: '#334E68' },
  his:      { label: 'provenance.his', icon: SyncAlt, bg: '#EFF4FF', fg: '#1D4ED8' },
  imported: { label: 'provenance.imported', icon: Upload, bg: '#EDF1F5', fg: '#334E68' },
  signed:   { label: 'provenance.signed', icon: Lock, bg: '#102A43', fg: '#FFFFFF' },
} satisfies Record<string, Look>;

export const alertLooks = {
  critical:  { label: 'alert.critical', icon: ErrorIcon, bg: '#FDECEE', fg: '#8A1626', border: '1px solid #F3B9C0' },
  attention: { label: 'alert.attention', icon: Warning, bg: '#FDF5E7', fg: '#7A4710', border: '1px solid #EFD5A6' },
  info:      { label: 'alert.info', icon: Info, bg: '#F7FAFC', fg: '#334E68', border: '1px solid #DFE7ED' },
} satisfies Record<string, Look>;

export const recordingLooks = {
  recording: { label: 'recording.rec', icon: Pause, bg: '#B42333', fg: '#FFFFFF' }, // icon unused: the blinking dot is the shape cue
  paused:    { label: 'recording.paused', icon: Pause, bg: '#FDF5E7', fg: '#7A4710' },
  declined:  { label: 'recording.declined', icon: DoNotDisturbOn, bg: '#EDF1F5', fg: '#334E68' },
  given:     { label: 'recording.given', icon: TaskAlt, bg: '#E6F4EF', fg: okOnTint },
  notAsked:  { label: 'recording.notAsked', icon: HelpOutline, bg: '#FDF5E7', fg: '#7A4710' },
} satisfies Record<string, Look>;

function Pill({ look, text, height = 28, radius = 999, children }: { look: Look; text?: string; height?: number; radius?: number; children?: ReactNode }) {
  const { t } = useI18n();
  const Icon = look.icon;
  return (
    <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.75, height, px: 1.25, borderRadius: `${radius}px`,
      bgcolor: look.bg, color: look.fg, border: look.border ?? 0, fontSize: 13, fontWeight: 700, whiteSpace: 'nowrap', lineHeight: 1 }}>
      {children ?? <Icon sx={{ fontSize: 16 }} aria-hidden />}
      {text ?? t(look.label)}
    </Box>
  );
}

export const StatusChip = ({ state }: { state: keyof typeof statusLooks }) => <Pill look={statusLooks[state]} />;

export const ProvenanceTag = ({ source }: { source: keyof typeof provenanceLooks }) =>
  <Pill look={provenanceLooks[source]} height={26} radius={6} />;

export function ClinicalAlert({ severity, children }: { severity: keyof typeof alertLooks; children: ReactNode }) {
  const { t } = useI18n();
  const look = alertLooks[severity], Icon = look.icon;
  return (
    <Box role={severity === 'critical' ? 'alert' : 'status'} sx={{ display: 'flex', gap: 1, alignItems: 'flex-start', p: 1.5, borderRadius: '10px',
      bgcolor: look.bg, color: look.fg, border: look.border }}>
      <Icon sx={{ fontSize: 20, mt: '2px' }} aria-hidden />
      <Box><Typography component="strong" sx={{ fontWeight: 800, fontSize: 14 }}>{t(look.label)}</Typography>
        <Typography sx={{ fontSize: 14, color: 'inherit' }}>{children}</Typography></Box>
    </Box>
  );
}

const blink = keyframes`0%,100%{opacity:1}50%{opacity:.35}`;

export function RecordingPill({ state, elapsedSeconds = 0 }: { state: keyof typeof recordingLooks; elapsedSeconds?: number }) {
  const { t } = useI18n();
  const look = recordingLooks[state];
  if (state !== 'recording') return <Pill look={look} />;
  const mm = String(Math.floor(elapsedSeconds / 60)).padStart(2, '0'), ss = String(elapsedSeconds % 60).padStart(2, '0');
  return (
    <Pill look={look} text={t('recording.rec', { time: `${mm}:${ss}` })}>
      <Box component="span" aria-hidden sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: '#FFFFFF',
        animation: `${blink} 1s ease-in-out infinite`, '@media (prefers-reduced-motion: reduce)': { animation: 'none' } }} />
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

// Unicode first-strong isolate: keeps Latin IDs and dates intact inside Arabic sentences.
const iso = (v: string) => `\u2068${v}\u2069`;

const allergyLook: Look = { label: 'banner.allergy', icon: Medication, bg: '#FDECEE', fg: '#8A1626' };
const noAllergyLook: Look = { label: 'banner.noAllergies', icon: Medication, bg: '#F1F5F8', fg: '#334E68' };
const unknownAllergyLook: Look = { label: 'banner.allergiesNotRecorded', icon: HelpOutline, bg: '#FDF5E7', fg: '#7A4710' };

/** Fixed patient identity banner for every clinical screen (Foundations §04). Allergies never collapse. */
export function IdentityBanner({ patient }: { patient: BannerPatient }) {
  const { t } = useI18n();
  const initials = patient.name.split(' ').map(p => p[0]).slice(0, 2).join('');
  return (
    <Box component="section" aria-label={t('banner.patient', { name: patient.name })} sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '8px 16px', p: 1.5,
      bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '12px', boxShadow: 'inset 4px 0 0 #0E9384' }}>
      <Box aria-hidden sx={{ width: 40, height: 40, borderRadius: '50%', bgcolor: '#102A43', color: '#fff', display: 'grid', placeItems: 'center', fontWeight: 800 }}>{initials}</Box>
      <Box>
        <Typography component="h2" sx={{ fontSize: 17, fontWeight: 800 }}>
          {patient.name} <Box component="span" sx={{ fontWeight: 500, color: 'text.secondary' }}>{patient.age} {patient.sex}</Box>
        </Typography>
        <Typography sx={{ font: "400 12px 'JetBrains Mono', monospace", color: 'text.secondary' }}>{t('banner.mrnDob', { mrn: iso(patient.mrn), dob: iso(patient.dob) })}</Typography>
      </Box>
      {patient.allergies === null ? <Pill look={unknownAllergyLook} />
        : patient.allergies.length === 0 ? <Pill look={noAllergyLook} />
        : patient.allergies.map(a => <Pill key={a.name} look={allergyLook}
            text={t('banner.allergy', { name: iso(a.name), status: t(`banner.allergy.${a.status}`) })} />)}
      {patient.consent && <RecordingPill state={patient.consent} />}
      {patient.encounter && <Typography sx={{ fontSize: 13, color: 'text.secondary' }}>{patient.encounter}</Typography>}
      {patient.synthetic && <Box component="span" sx={{ font: "600 10px 'JetBrains Mono', monospace", color: '#975A16', bgcolor: '#FDF5E7', px: 1, py: 0.5, borderRadius: '4px' }}>{t('banner.synthetic')}</Box>}
    </Box>
  );
}

export const systemStates = {
  sessionExpired: { title: 'state.sessionExpired.title', body: 'state.sessionExpired.body', cta: 'state.sessionExpired.cta', icon: LockClock, color: '#334E68' },
  noAccess:       { title: 'err.403', body: 'state.noAccess.body', cta: 'state.noAccess.cta', icon: Block, color: '#8A1626' },
  notFound:       { title: 'state.notFound.title', body: 'state.notFound.body', cta: 'state.notFound.cta', icon: TravelExplore, color: '#334E68' },
  offline:        { title: 'state.offline.title', body: 'state.offline.body', cta: 'state.offline.cta', icon: WifiOff, color: '#975A16' },
} satisfies Record<string, { title: MessageKey; body: MessageKey; cta: MessageKey; icon: typeof Send; color: string }>;

/** F-05 system states: one message, one action, announced to assistive technology. */
export function SystemState({ state, onAction, facility = '' }: { state: keyof typeof systemStates; onAction: () => void; facility?: string }) {
  const { t } = useI18n();
  const s = systemStates[state], Icon = s.icon;
  return (
    <Box role={state === 'offline' ? 'status' : 'alert'} sx={{ display: 'flex', flexDirection: 'column', gap: 1.25, alignItems: 'flex-start', p: 3,
      maxWidth: 420, bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '16px' }}>
      <Icon sx={{ fontSize: 30, color: s.color }} aria-hidden />
      <Typography component="h1" sx={{ fontSize: 17, fontWeight: 800 }}>{t(s.title)}</Typography>
      <Typography sx={{ fontSize: 14, color: 'text.secondary' }}>{t(s.body, { facility })}</Typography>
      <Button variant="contained" onClick={onAction} sx={{ mt: 1, bgcolor: '#102A43', '&:hover': { bgcolor: '#0B1622' } }}>{t(s.cta)}</Button>
    </Box>
  );
}
