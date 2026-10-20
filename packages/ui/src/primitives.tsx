import type { ReactNode } from 'react';
import { Box, Typography } from '@mui/material';
import { keyframes } from '@mui/material/styles';
import {
  Send, Colorize, Inbox, Visibility, Call, MarkChatRead, TaskAlt,
  AutoAwesome, Verified, Person, SyncAlt, Upload, Lock,
  Error as ErrorIcon, Warning, Info, Pause, DoNotDisturbOn, Medication, HelpOutline,
} from '@mui/icons-material';

// Every clinical state is icon + words + colour, never colour alone (Foundations §01).
// Values come from Handoff §1 and Foundations §04; see docs/02-design.
// Deviation: success text on the #E6F4EF tint is #07774F, not #087F5B (4.42:1 fails WCAG AA; 4.93:1 passes).
const okOnTint = '#07774F';
export type Look = { label: string; icon: typeof Send; bg: string; fg: string; border?: string };

export const statusLooks = {
  ordered:   { label: 'Ordered', icon: Send, bg: '#EFF4FF', fg: '#1D4ED8' },
  collected: { label: 'Sample collected', icon: Colorize, bg: '#EFF4FF', fg: '#1D4ED8' },
  received:  { label: 'Result received', icon: Inbox, bg: '#FDF5E7', fg: '#7A4710' },
  reviewed:  { label: 'Reviewed by doctor', icon: Visibility, bg: '#E7F6F4', fg: '#0F766E' },
  contacted: { label: 'Patient contacted', icon: Call, bg: '#EEF4EF', fg: '#3E6A4D' },
  delivered: { label: 'Message delivered ≠ done', icon: MarkChatRead, bg: '#F1F5F8', fg: '#506579' },
  completed: { label: 'Follow-up completed', icon: TaskAlt, bg: '#087F5B', fg: '#FFFFFF' },
} satisfies Record<string, Look>;

export const provenanceLooks = {
  ai:       { label: 'AI draft · not reviewed', icon: AutoAwesome, bg: '#F2EFFC', fg: '#5B3FC4', border: '1px dashed #A897E8' },
  verified: { label: 'Clinician verified', icon: Verified, bg: '#E6F4EF', fg: okOnTint },
  patient:  { label: 'Patient reported', icon: Person, bg: '#F1F5F8', fg: '#334E68' },
  his:      { label: 'Synced from HIS', icon: SyncAlt, bg: '#EFF4FF', fg: '#1D4ED8' },
  imported: { label: 'Imported', icon: Upload, bg: '#EDF1F5', fg: '#334E68' },
  signed:   { label: 'Signed · read-only', icon: Lock, bg: '#102A43', fg: '#FFFFFF' },
} satisfies Record<string, Look>;

export const alertLooks = {
  critical:  { label: 'Critical · unacknowledged', icon: ErrorIcon, bg: '#FDECEE', fg: '#8A1626', border: '1px solid #F3B9C0' },
  attention: { label: 'Attention · uncertain', icon: Warning, bg: '#FDF5E7', fg: '#7A4710', border: '1px solid #EFD5A6' },
  info:      { label: 'Info', icon: Info, bg: '#F7FAFC', fg: '#334E68', border: '1px solid #DFE7ED' },
} satisfies Record<string, Look>;

export const recordingLooks = {
  recording: { label: 'REC', icon: Pause, bg: '#B42333', fg: '#FFFFFF' }, // icon unused: the blinking dot is the shape cue
  paused:    { label: 'Paused', icon: Pause, bg: '#FDF5E7', fg: '#7A4710' },
  declined:  { label: 'Declined · manual note', icon: DoNotDisturbOn, bg: '#EDF1F5', fg: '#334E68' },
  given:     { label: 'Recording consent · given', icon: TaskAlt, bg: '#E6F4EF', fg: okOnTint },
  notAsked:  { label: 'Recording consent · not yet asked', icon: HelpOutline, bg: '#FDF5E7', fg: '#7A4710' },
} satisfies Record<string, Look>;

function Pill({ look, label, height = 28, radius = 999, children }: { look: Look; label?: string; height?: number; radius?: number; children?: ReactNode }) {
  const Icon = look.icon;
  return (
    <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.75, height, px: 1.25, borderRadius: `${radius}px`,
      bgcolor: look.bg, color: look.fg, border: look.border ?? 0, fontSize: 13, fontWeight: 700, whiteSpace: 'nowrap', lineHeight: 1 }}>
      {children ?? <Icon sx={{ fontSize: 16 }} aria-hidden />}
      {label ?? look.label}
    </Box>
  );
}

export const StatusChip = ({ state }: { state: keyof typeof statusLooks }) => <Pill look={statusLooks[state]} />;

export const ProvenanceTag = ({ source }: { source: keyof typeof provenanceLooks }) =>
  <Pill look={provenanceLooks[source]} height={26} radius={6} />;

export function ClinicalAlert({ severity, children }: { severity: keyof typeof alertLooks; children: ReactNode }) {
  const look = alertLooks[severity], Icon = look.icon;
  return (
    <Box role={severity === 'critical' ? 'alert' : 'status'} sx={{ display: 'flex', gap: 1, alignItems: 'flex-start', p: 1.5, borderRadius: '10px',
      bgcolor: look.bg, color: look.fg, border: look.border }}>
      <Icon sx={{ fontSize: 20, mt: '2px' }} aria-hidden />
      <Box><Typography component="strong" sx={{ fontWeight: 800, fontSize: 14 }}>{look.label}</Typography>
        <Typography sx={{ fontSize: 14, color: 'inherit' }}>{children}</Typography></Box>
    </Box>
  );
}

const blink = keyframes`0%,100%{opacity:1}50%{opacity:.35}`;

export function RecordingPill({ state, elapsedSeconds = 0 }: { state: keyof typeof recordingLooks; elapsedSeconds?: number }) {
  const look = recordingLooks[state];
  if (state !== 'recording') return <Pill look={look} />;
  const mm = String(Math.floor(elapsedSeconds / 60)).padStart(2, '0'), ss = String(elapsedSeconds % 60).padStart(2, '0');
  return (
    <Pill look={look} label={`REC ${mm}:${ss}`}>
      <Box component="span" aria-hidden sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: '#FFFFFF',
        animation: `${blink} 1s ease-in-out infinite`, '@media (prefers-reduced-motion: reduce)': { animation: 'none' } }} />
    </Pill>
  );
}

export type Allergy = { name: string; status: 'verified' | 'patient reported' | 'unknown' };
export type BannerPatient = {
  name: string; age: number; sex: 'F' | 'M' | 'X'; mrn: string; dob: string;
  /** null = allergies not recorded; [] = no known allergies (explicitly confirmed). Never conflate the two. */
  allergies: Allergy[] | null;
  consent?: keyof typeof recordingLooks; encounter?: string; synthetic?: boolean;
};

const allergyLook: Look = { label: '', icon: Medication, bg: '#FDECEE', fg: '#8A1626' };
const noAllergyLook: Look = { label: 'No known allergies', icon: Medication, bg: '#F1F5F8', fg: '#334E68' };
const unknownAllergyLook: Look = { label: 'Allergies not recorded', icon: HelpOutline, bg: '#FDF5E7', fg: '#7A4710' };

/** Fixed patient identity banner for every clinical screen (Foundations §04). Allergies never collapse. */
export function IdentityBanner({ patient }: { patient: BannerPatient }) {
  const initials = patient.name.split(' ').map(p => p[0]).slice(0, 2).join('');
  return (
    <Box component="section" aria-label={`Patient ${patient.name}`} sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '8px 16px', p: 1.5,
      bgcolor: 'background.paper', border: '1px solid', borderColor: 'divider', borderRadius: '12px', boxShadow: 'inset 4px 0 0 #0E9384' }}>
      <Box aria-hidden sx={{ width: 40, height: 40, borderRadius: '50%', bgcolor: '#102A43', color: '#fff', display: 'grid', placeItems: 'center', fontWeight: 800 }}>{initials}</Box>
      <Box>
        <Typography component="h2" sx={{ fontSize: 17, fontWeight: 800 }}>
          {patient.name} <Box component="span" sx={{ fontWeight: 500, color: 'text.secondary' }}>{patient.age} {patient.sex}</Box>
        </Typography>
        <Typography sx={{ font: "400 12px 'JetBrains Mono', monospace", color: 'text.secondary' }}>MRN {patient.mrn} · DOB {patient.dob}</Typography>
      </Box>
      {patient.allergies === null ? <Pill look={unknownAllergyLook} />
        : patient.allergies.length === 0 ? <Pill look={noAllergyLook} />
        : patient.allergies.map(a => <Pill key={a.name} look={allergyLook} label={`Allergy: ${a.name} · ${a.status}`} />)}
      {patient.consent && <RecordingPill state={patient.consent} />}
      {patient.encounter && <Typography sx={{ fontSize: 13, color: 'text.secondary' }}>{patient.encounter}</Typography>}
      {patient.synthetic && <Box component="span" sx={{ font: "600 10px 'JetBrains Mono', monospace", color: '#975A16', bgcolor: '#FDF5E7', px: 1, py: 0.5, borderRadius: '4px' }}>SYNTHETIC</Box>}
    </Box>
  );
}
