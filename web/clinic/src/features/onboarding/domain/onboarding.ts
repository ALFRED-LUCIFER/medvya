import { z } from 'zod';
import type { Country } from '@medvya/i18n';

// Shapes mirror src/Modules/Tenancy/Medvya.Tenancy.Application/Onboarding.cs. The server validates every field and is the authority;
// the schema below only answers "required" at once, without a round trip.
export type Mode = 'Solo' | 'Clinic';
export type Window = { start: string; end: string };
export type OnboardingData = {
  profile?: { fullName?: string; mode?: Mode; specialty?: string; qualifications?: string; languages?: string };
  licence?: { authority?: string; number?: string; certificateName?: string };
  clinic?: { name?: string; address?: string; registrationNumber?: string; taxId?: string };
  hours?: { weekdays?: Window[]; saturday?: Window[]; slotMinutes?: number; newVisitFee?: number; followUpFee?: number };
  templates?: string[];
  team?: { email: string; role: 'Receptionist' | 'Nurse' }[];
};
export type Authority = { code: string; name: string };
export type Rules = { currency: 'INR' | 'AED'; authorities: Authority[]; slotMinutes: number[]; templates: string[] };
export type Draft = { country: Country; step: number; data: OnboardingData; rules: Rules };
export type FinishResult = { membershipId: string; role: 'SoloDoctor' | 'PracticeAdmin'; invitationsSent: number; invitationsFailed: number };

export const stepIds = ['profile', 'licence', 'clinic', 'hours', 'templates', 'team'] as const;
export type StepId = (typeof stepIds)[number];

/** Step numbers (1-6) a mode goes through: an owner who is not a doctor skips the licence step. */
export const stepsFor = (mode: Mode): number[] => (mode === 'Clinic' ? [1, 3, 4, 5, 6] : [1, 2, 3, 4, 5, 6]);

/** The form: flat strings, as typed. Fees are whole currency units here and minor units (x100) on the wire. */
export type WizardForm = {
  mode: Mode; fullName: string; specialty: string; qualifications: string; languages: string;
  authority: string; number: string; certificateName: string;
  clinicName: string; address: string; registrationNumber: string; taxId: string;
  weekdayStart1: string; weekdayEnd1: string; weekdayStart2: string; weekdayEnd2: string; saturdayStart: string; saturdayEnd: string;
  slotMinutes: string; newVisitFee: string; followUpFee: string;
  templates: string[]; receptionistEmail: string; nurseEmail: string;
};
export type FormField = keyof WizardForm;

export const stepFields: Record<number, FormField[]> = {
  1: ['mode', 'fullName', 'specialty', 'qualifications', 'languages'],
  2: ['authority', 'number', 'certificateName'],
  3: ['clinicName', 'address', 'registrationNumber', 'taxId'],
  4: ['weekdayStart1', 'weekdayEnd1', 'weekdayStart2', 'weekdayEnd2', 'saturdayStart', 'saturdayEnd', 'slotMinutes', 'newVisitFee', 'followUpFee'],
  5: ['templates'],
  6: ['receptionistEmail', 'nurseEmail'],
};

/** Design defaults (F-04): weekdays 09:00-13:00 and 17:00-20:00, 20-minute slots, fees per country. */
const defaults: Record<Country, Pick<WizardForm, 'newVisitFee' | 'followUpFee'>> = {
  IN: { newVisitFee: '1000', followUpFee: '600' },
  AE: { newVisitFee: '350', followUpFee: '250' },
};

export const emptyForm = (country: Country): WizardForm => ({
  mode: 'Solo', fullName: '', specialty: '', qualifications: '', languages: '',
  authority: '', number: '', certificateName: '',
  clinicName: '', address: '', registrationNumber: '', taxId: '',
  weekdayStart1: '09:00', weekdayEnd1: '13:00', weekdayStart2: '17:00', weekdayEnd2: '20:00', saturdayStart: '', saturdayEnd: '',
  slotMinutes: '20', ...defaults[country], templates: [], receptionistEmail: '', nurseEmail: '',
});

const windows = (start1: string, end1: string, start2 = '', end2 = ''): Window[] =>
  [{ start: start1, end: end1 }, { start: start2, end: end2 }].filter(w => w.start || w.end);

const minor = (major: string) => (major.trim() === '' ? undefined : Math.round(Number(major) * 100));

/** Wizard values to the draft the server stores. Blank optional values are omitted: unknowns stay blank, never guessed. */
export function toData(f: WizardForm): OnboardingData {
  const team = [
    ...(f.receptionistEmail.trim() ? [{ email: f.receptionistEmail.trim(), role: 'Receptionist' as const }] : []),
    ...(f.nurseEmail.trim() ? [{ email: f.nurseEmail.trim(), role: 'Nurse' as const }] : []),
  ];
  return {
    profile: { fullName: f.fullName, mode: f.mode, specialty: f.specialty, qualifications: f.qualifications, languages: f.languages },
    licence: f.mode === 'Solo' ? { authority: f.authority, number: f.number, certificateName: f.certificateName } : undefined,
    clinic: { name: f.clinicName, address: f.address, registrationNumber: f.registrationNumber, taxId: f.taxId },
    hours: {
      weekdays: windows(f.weekdayStart1, f.weekdayEnd1, f.weekdayStart2, f.weekdayEnd2), saturday: windows(f.saturdayStart, f.saturdayEnd),
      slotMinutes: f.slotMinutes ? Number(f.slotMinutes) : undefined, newVisitFee: minor(f.newVisitFee), followUpFee: minor(f.followUpFee),
    },
    templates: f.templates,
    team,
  };
}

const major = (minorUnits: number | undefined, fallback: string) => (minorUnits === undefined ? fallback : String(minorUnits / 100));

/** The saved draft back into the form; sections never saved keep the design defaults. */
export function toForm(data: OnboardingData, country: Country): WizardForm {
  const base = emptyForm(country);
  const w = data.hours?.weekdays ?? [], s = data.hours?.saturday?.[0];
  const hasHours = data.hours !== undefined;
  return {
    ...base,
    mode: data.profile?.mode ?? base.mode, fullName: data.profile?.fullName ?? '', specialty: data.profile?.specialty ?? '',
    qualifications: data.profile?.qualifications ?? '', languages: data.profile?.languages ?? '',
    authority: data.licence?.authority ?? '', number: data.licence?.number ?? '', certificateName: data.licence?.certificateName ?? '',
    clinicName: data.clinic?.name ?? '', address: data.clinic?.address ?? '', registrationNumber: data.clinic?.registrationNumber ?? '', taxId: data.clinic?.taxId ?? '',
    weekdayStart1: hasHours ? w[0]?.start ?? '' : base.weekdayStart1, weekdayEnd1: hasHours ? w[0]?.end ?? '' : base.weekdayEnd1,
    weekdayStart2: hasHours ? w[1]?.start ?? '' : base.weekdayStart2, weekdayEnd2: hasHours ? w[1]?.end ?? '' : base.weekdayEnd2,
    saturdayStart: s?.start ?? '', saturdayEnd: s?.end ?? '',
    slotMinutes: data.hours?.slotMinutes ? String(data.hours.slotMinutes) : base.slotMinutes,
    newVisitFee: major(data.hours?.newVisitFee, base.newVisitFee), followUpFee: major(data.hours?.followUpFee, base.followUpFee),
    templates: data.templates ?? [],
    receptionistEmail: data.team?.find(m => m.role === 'Receptionist')?.email ?? '', nurseEmail: data.team?.find(m => m.role === 'Nurse')?.email ?? '',
  };
}

const anyText = z.string();
const base = z.object({
  mode: z.enum(['Solo', 'Clinic']), templates: z.array(z.string()),
  fullName: anyText, specialty: anyText, qualifications: anyText, languages: anyText, authority: anyText, number: anyText, certificateName: anyText,
  clinicName: anyText, address: anyText, registrationNumber: anyText, taxId: anyText, weekdayStart1: anyText, weekdayEnd1: anyText,
  weekdayStart2: anyText, weekdayEnd2: anyText, saturdayStart: anyText, saturdayEnd: anyText, slotMinutes: anyText, newVisitFee: anyText,
  followUpFee: anyText, receptionistEmail: anyText, nurseEmail: anyText,
});

/** What one step requires, answered at once without a round trip. The server checks formats, ranges and the country's rules. */
export const stepSchema = (step: number) => base.superRefine((f, ctx) => {
  const need = (field: FormField, value: string, when = true) => {
    if (when && value.trim() === '') ctx.addIssue({ code: 'custom', path: [field], message: 'required' });
  };
  if (step === 1) { need('fullName', f.fullName); need('specialty', f.specialty, f.mode === 'Solo'); }
  if (step === 2) { need('authority', f.authority); need('number', f.number); }
  if (step === 3) { need('clinicName', f.clinicName); need('address', f.address); }
  if (step === 4) {
    need('weekdayStart1', f.weekdayStart1 + f.saturdayStart); // at least one open window
    need('slotMinutes', f.slotMinutes); need('newVisitFee', f.newVisitFee); need('followUpFee', f.followUpFee);
  }
});

/** Server error path (e.g. "licence.number", "team.0.email") to the form field it belongs under. */
export function formFieldOf(path: string, form: WizardForm): FormField | null {
  const direct: Record<string, FormField> = {
    'profile.mode': 'mode', 'profile.fullName': 'fullName', 'profile.specialty': 'specialty', 'profile.qualifications': 'qualifications',
    'profile.languages': 'languages', 'licence.authority': 'authority', 'licence.number': 'number', 'licence.certificate': 'certificateName',
    'clinic.name': 'clinicName', 'clinic.address': 'address', 'clinic.registrationNumber': 'registrationNumber', 'clinic.taxId': 'taxId',
    'hours.weekdays': 'weekdayStart1', 'hours.saturday': 'saturdayStart', 'hours.slotMinutes': 'slotMinutes',
    'hours.newVisitFee': 'newVisitFee', 'hours.followUpFee': 'followUpFee', templates: 'templates', team: 'receptionistEmail',
  };
  if (path in direct) return direct[path]!;
  const m = /^team\.(\d+)\.(email|role)$/.exec(path);
  if (!m) return null;
  // The server numbers team members as sent: the receptionist first when both are filled in.
  const slots: FormField[] = [form.receptionistEmail.trim() ? 'receptionistEmail' : null, form.nurseEmail.trim() ? 'nurseEmail' : null]
    .filter((x): x is FormField => x !== null);
  return slots[Number(m[1])] ?? null;
}

/** The first step (in wizard order) that owns a server error path; used when finishing finds a problem in an earlier step. */
export function stepOfPath(path: string): number {
  const area = path.split('.')[0];
  return area === 'profile' ? 1 : area === 'licence' ? 2 : area === 'clinic' ? 3 : area === 'hours' ? 4 : area === 'templates' ? 5 : 6;
}

/** The codes the server sends, each with a translated message. */
export const errorCodes = ['required', 'tooLong', 'format', 'unknown', 'range', 'overlap', 'duplicate'] as const;
export const errorCode = (code: string): (typeof errorCodes)[number] => errorCodes.find(c => c === code) ?? 'format';

/** Note templates the server offers; ids we have no label for are not shown. */
export const templateIds = ['general-opd', 'diabetes-review', 'hypertension', 'paediatrics', 'antenatal', 'teleconsult'] as const;
export const templateIdOf = (id: string) => templateIds.find(t => t === id);
