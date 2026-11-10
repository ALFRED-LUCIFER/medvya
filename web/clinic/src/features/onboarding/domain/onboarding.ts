import { z } from 'zod';
import type { Country } from '@medvya/i18n';

// Shapes mirror src/Modules/Tenancy/Medvya.Tenancy.Application/Onboarding.cs. The server validates every field and is the authority;
// the schema below only answers "required" at once, without a round trip.
export type Mode = 'Solo' | 'Clinic';
export type Window = { start: string; end: string };
export const weekDays = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'] as const;
export type Day = (typeof weekDays)[number];
export type TeamRole = 'Receptionist' | 'Nurse';
export type Member = { email: string; role: TeamRole };
export const patientStarts = ['Upload', 'Move', 'Fresh'] as const;
export type PatientStart = (typeof patientStarts)[number];
export type OnboardingData = {
  profile?: { fullName?: string; mode?: Mode; specialty?: string; specialties?: string[]; qualifications?: string; languages?: string };
  licence?: { authority?: string; number?: string; certificateName?: string; attested?: boolean };
  clinic?: { name?: string; address?: string; registrationNumber?: string; taxId?: string };
  hours?: { weekdays?: Window[]; saturday?: Window[]; slotMinutes?: number; newVisitFee?: number; followUpFee?: number; days?: Day[] };
  templates?: string[];
  team?: Member[];
  patients?: PatientStart;
};
export type Option = { code: string; name: string };
export type Rules = { currency: 'INR' | 'AED'; authorities: Option[]; slotMinutes: number[]; templates: string[]; specialties: Option[] };
export type Draft = { country: Country; step: number; data: OnboardingData; rules: Rules };
export type FinishResult = { membershipId: string; role: 'SoloDoctor' | 'PracticeAdmin'; invitationsSent: number; invitationsFailed: number };

/** The 6 Nov steps; the ready screen follows finishing and is not a step the server checks. */
export const stepIds = ['practice', 'hours', 'team', 'patients', 'payments'] as const;
export const steps = [1, 2, 3, 4, 5];
/** Steps that may be left for later (the design's "Skip for now"): team, patients, payments. */
export const skippable = (step: number) => step >= 3;

/** The form: flat strings, as typed. Fees are whole currency units here and minor units (x100) on the wire. */
export type WizardForm = {
  mode: Mode; fullName: string; clinicName: string; address: string; specialties: string[];
  authority: string; number: string; attested: boolean; qualifications: string; languages: string; registrationNumber: string; taxId: string;
  days: Day[]; weekdayStart1: string; weekdayEnd1: string; weekdayStart2: string; weekdayEnd2: string; saturdayStart: string; saturdayEnd: string;
  slotMinutes: string; newVisitFee: string; followUpFee: string;
  team: Member[]; inviteEmail: string; inviteRole: TeamRole; patients: PatientStart | '';
  // Kept as saved, not shown: the free-text specialty and certificate name of earlier drafts, and note templates (used from P2/P3).
  specialty: string; certificateName: string; templates: string[];
};
export type FormField = keyof WizardForm;

export const stepFields: Record<number, FormField[]> = {
  1: ['mode', 'fullName', 'clinicName', 'address', 'specialties', 'authority', 'number', 'attested', 'qualifications', 'languages', 'registrationNumber', 'taxId'],
  2: ['days', 'weekdayStart1', 'weekdayEnd1', 'weekdayStart2', 'weekdayEnd2', 'saturdayStart', 'saturdayEnd', 'slotMinutes', 'newVisitFee', 'followUpFee'],
  3: ['team', 'inviteEmail'],
  4: ['patients'],
  5: [],
};

/** Design defaults (6 Nov): open Monday to Saturday, the country's usual split day, 15-minute appointments, fees per country. */
const defaults: Record<Country, Pick<WizardForm, 'weekdayStart1' | 'weekdayEnd1' | 'weekdayStart2' | 'weekdayEnd2' | 'saturdayStart' | 'saturdayEnd' | 'newVisitFee' | 'followUpFee'>> = {
  IN: { weekdayStart1: '09:00', weekdayEnd1: '13:00', weekdayStart2: '17:00', weekdayEnd2: '20:00', saturdayStart: '09:00', saturdayEnd: '14:00', newVisitFee: '800', followUpFee: '600' },
  AE: { weekdayStart1: '08:00', weekdayEnd1: '14:00', weekdayStart2: '16:00', weekdayEnd2: '21:00', saturdayStart: '09:00', saturdayEnd: '15:00', newVisitFee: '350', followUpFee: '250' },
};
const openByDefault: Day[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

export const emptyForm = (country: Country): WizardForm => ({
  mode: 'Solo', fullName: '', clinicName: '', address: '', specialties: [],
  authority: '', number: '', attested: false, qualifications: '', languages: '', registrationNumber: '', taxId: '',
  days: openByDefault, ...defaults[country], slotMinutes: '15',
  team: [], inviteEmail: '', inviteRole: 'Receptionist', patients: '',
  specialty: '', certificateName: '', templates: [],
});

const windows = (start1: string, end1: string, start2 = '', end2 = ''): Window[] =>
  [{ start: start1, end: end1 }, { start: start2, end: end2 }].filter(w => w.start || w.end);

const minor = (major: string) => (major.trim() === '' ? undefined : Math.round(Number(major) * 100));

/** Wizard values to the draft the server stores. Blank optional values are omitted: unknowns stay blank, never guessed. */
export function toData(f: WizardForm): OnboardingData {
  return {
    profile: { fullName: f.fullName, mode: f.mode, specialty: f.specialty, specialties: f.specialties, qualifications: f.qualifications, languages: f.languages },
    licence: f.mode === 'Solo' ? { authority: f.authority, number: f.number, certificateName: f.certificateName, attested: f.attested } : undefined,
    clinic: { name: f.clinicName, address: f.address, registrationNumber: f.registrationNumber, taxId: f.taxId },
    hours: {
      weekdays: windows(f.weekdayStart1, f.weekdayEnd1, f.weekdayStart2, f.weekdayEnd2),
      // Saturday's own hours count only when Saturday is open; otherwise it would open with them.
      saturday: f.days.includes('Saturday') ? windows(f.saturdayStart, f.saturdayEnd) : [], days: weekDays.filter(d => f.days.includes(d)),
      slotMinutes: f.slotMinutes ? Number(f.slotMinutes) : undefined, newVisitFee: minor(f.newVisitFee), followUpFee: minor(f.followUpFee),
    },
    templates: f.templates,
    team: f.team,
    patients: f.patients || undefined,
  };
}

const major = (minorUnits: number | undefined, fallback: string) => (minorUnits === undefined ? fallback : String(minorUnits / 100));

/** The saved draft back into the form; sections never saved keep the design defaults. */
export function toForm(data: OnboardingData, country: Country): WizardForm {
  const base = emptyForm(country);
  const h = data.hours;
  const w = h?.weekdays ?? [], s = h?.saturday?.[0];
  // Drafts saved before the day switches: Monday to Friday, plus Saturday when it had hours (the server reads them the same way).
  const legacyDays: Day[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', ...(s ? ['Saturday' as const] : [])];
  return {
    ...base,
    mode: data.profile?.mode ?? base.mode, fullName: data.profile?.fullName ?? '', specialty: data.profile?.specialty ?? '',
    specialties: data.profile?.specialties ?? [], qualifications: data.profile?.qualifications ?? '', languages: data.profile?.languages ?? '',
    authority: data.licence?.authority ?? '', number: data.licence?.number ?? '', attested: data.licence?.attested ?? false, certificateName: data.licence?.certificateName ?? '',
    clinicName: data.clinic?.name ?? '', address: data.clinic?.address ?? '', registrationNumber: data.clinic?.registrationNumber ?? '', taxId: data.clinic?.taxId ?? '',
    ...(h && {
      days: h.days ?? legacyDays,
      weekdayStart1: w[0]?.start ?? '', weekdayEnd1: w[0]?.end ?? '', weekdayStart2: w[1]?.start ?? '', weekdayEnd2: w[1]?.end ?? '',
      saturdayStart: s?.start ?? base.saturdayStart, saturdayEnd: s?.end ?? base.saturdayEnd,
    }),
    slotMinutes: h?.slotMinutes ? String(h.slotMinutes) : base.slotMinutes,
    newVisitFee: major(h?.newVisitFee, base.newVisitFee), followUpFee: major(h?.followUpFee, base.followUpFee),
    templates: data.templates ?? [], team: data.team ?? [], patients: data.patients ?? '',
  };
}

/** Adds a colleague to the team step, or says why not (the same codes the server uses; it checks again). */
export function addMember(team: Member[], email: string, role: TeamRole): { team: Member[] } | { error: 'required' | 'format' | 'duplicate' | 'range' } {
  const e = email.trim();
  if (!e) return { error: 'required' };
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(e)) return { error: 'format' };
  if (team.some(m => m.email.toLowerCase() === e.toLowerCase())) return { error: 'duplicate' };
  if (team.length >= maxTeam) return { error: 'range' };
  return { team: [...team, { email: e, role }] };
}
export const maxTeam = 5;

const anyText = z.string();
const base = z.object({
  mode: z.enum(['Solo', 'Clinic']), attested: z.boolean(), specialties: z.array(z.string()), days: z.array(z.enum(weekDays)), templates: z.array(z.string()),
  team: z.array(z.object({ email: z.string(), role: z.enum(['Receptionist', 'Nurse']) })), inviteRole: z.enum(['Receptionist', 'Nurse']),
  patients: z.enum(['', ...patientStarts]),
  fullName: anyText, clinicName: anyText, address: anyText, authority: anyText, number: anyText, qualifications: anyText, languages: anyText,
  registrationNumber: anyText, taxId: anyText, weekdayStart1: anyText, weekdayEnd1: anyText, weekdayStart2: anyText, weekdayEnd2: anyText,
  saturdayStart: anyText, saturdayEnd: anyText, slotMinutes: anyText, newVisitFee: anyText, followUpFee: anyText, inviteEmail: anyText,
  specialty: anyText, certificateName: anyText,
});

/** What one step requires, answered at once without a round trip. The server checks formats, ranges and the country's rules. */
export const stepSchema = (step: number) => base.superRefine((f, ctx) => {
  const need = (field: FormField, ok: boolean, message = 'required') => { if (!ok) ctx.addIssue({ code: 'custom', path: [field], message }); };
  const filled = (v: string) => v.trim() !== '';
  if (step === 1) {
    need('fullName', filled(f.fullName)); need('clinicName', filled(f.clinicName)); need('address', filled(f.address));
    if (f.mode === 'Solo') { need('specialties', f.specialties.length > 0 || filled(f.specialty)); need('authority', filled(f.authority)); need('number', filled(f.number)); need('attested', f.attested); }
  }
  if (step === 2) {
    need('days', f.days.length > 0);
    need('weekdayStart1', filled(f.weekdayStart1 + f.weekdayEnd1) || f.days.every(d => d === 'Saturday')); // the usual hours open every day but Saturday
    need('slotMinutes', filled(f.slotMinutes)); need('newVisitFee', filled(f.newVisitFee)); need('followUpFee', filled(f.followUpFee));
  }
  // An address typed but not added with Invite would be lost silently.
  if (step === 3) need('inviteEmail', !filled(f.inviteEmail), 'pending');
});

/** Server error path (e.g. "licence.number", "team.0.email") to the form field it belongs under. */
export function formFieldOf(path: string): FormField | `team.${number}.email` | null {
  const direct: Record<string, FormField> = {
    'profile.mode': 'mode', 'profile.fullName': 'fullName', 'profile.specialty': 'specialties', 'profile.specialties': 'specialties',
    'profile.qualifications': 'qualifications', 'profile.languages': 'languages', 'licence.authority': 'authority', 'licence.number': 'number', 'licence.attested': 'attested',
    'licence.certificate': 'number', 'clinic.name': 'clinicName', 'clinic.address': 'address', 'clinic.registrationNumber': 'registrationNumber',
    'clinic.taxId': 'taxId', 'hours.days': 'days', 'hours.weekdays': 'weekdayStart1', 'hours.saturday': 'saturdayStart', 'hours.slotMinutes': 'slotMinutes',
    'hours.newVisitFee': 'newVisitFee', 'hours.followUpFee': 'followUpFee', templates: 'slotMinutes', team: 'inviteEmail', patients: 'patients',
  };
  if (path in direct) return direct[path]!;
  const m = /^team\.(\d+)\.(email|role)$/.exec(path);
  return m ? `team.${Number(m[1])}.email` : null;
}

/** The step that owns a server error path; used when finishing finds a problem in an earlier step. */
export function stepOfPath(path: string): number {
  const area = path.split('.')[0];
  return area === 'hours' || area === 'templates' ? 2 : area === 'team' ? 3 : area === 'patients' ? 4 : 1;
}

/** The codes the server sends, each with a translated message. */
export const errorCodes = ['required', 'tooLong', 'format', 'unknown', 'range', 'overlap', 'duplicate', 'pending', 'link'] as const;
export const errorCode = (code: string): (typeof errorCodes)[number] => errorCodes.find(c => c === code) ?? 'format';

/** Specialty codes we have a label for; others are not shown. */
export const specialtyIds = ['general-practice', 'paediatrics', 'gynaecology', 'dermatology', 'dental', 'ent', 'orthopaedics', 'physiotherapy', 'diabetes', 'cardiology'] as const;
export const specialtyIdOf = (code: string) => specialtyIds.find(s => s === code);

/** Step 5: what a practice connects in each country. shortcut: none has a back end yet, so each one is "later, from Settings". */
export const connections: Record<Country, readonly string[]> = { IN: ['upi', 'whatsapp', 'abha', 'nhcx'], AE: ['card', 'whatsapp', 'eclaimlink', 'riayati'] };

/** The live booking-page preview (6 Nov design): what patients would see from what has been typed so far. */
export function preview(f: WizardForm) {
  const open = weekDays.filter(d => f.days.includes(d));
  const usual = open.some(d => d !== 'Saturday') ? windows(f.weekdayStart1, f.weekdayEnd1, f.weekdayStart2, f.weekdayEnd2) : windows(f.saturdayStart, f.saturdayEnd);
  const shown = usual.filter(w => w.start && w.end);
  const len = Number(f.slotMinutes) || 0;
  const [h = 0, m = 0] = (shown[0]?.start ?? '').split(':').map(Number);
  const slots = len && shown[0] ? [0, 1, 2, 4, 5, 8].map(k => h * 60 + m + k * len).filter(t => t < 24 * 60)
    .map(t => `${String(Math.floor(t / 60)).padStart(2, '0')}:${String(t % 60).padStart(2, '0')}`) : [];
  return {
    initials: f.clinicName.trim().split(/\s+/).filter(Boolean).slice(0, 2).map(w => w[0]!.toUpperCase()).join(''),
    name: f.clinicName.trim(), city: f.address.split(',').at(-1)?.trim() ?? '',
    specialties: f.specialties.slice(0, 3),
    firstDay: open[0] ?? null, lastDay: open.at(-1) ?? null, hours: shown.map(w => `${w.start}–${w.end}`).join(' · '),
    fee: Number.isFinite(minor(f.newVisitFee)) ? minor(f.newVisitFee)! : null, slotMinutes: len, slots, // half-typed or not a number: no fee line
  };
}
