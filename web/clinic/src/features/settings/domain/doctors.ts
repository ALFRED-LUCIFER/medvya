import { z } from 'zod';

// Mirrors src/Modules/Tenancy/Medvya.Tenancy.Application/IDoctors.cs (the server is the authority; these checks only save a round trip).
export type Authority = { code: string; name: string };
export type Doctor = {
  id: string;
  fullName: string;
  specialty: string | null;
  qualifications: string | null;
  languages: string | null;
  facilityId: string;
  facilityName: string;
  status: 'Invited' | 'Active' | 'Suspended' | 'Offboarded';
  authority: string;
  number: string;
  category: string | null;
  expiresOn: string | null;
  /** Past its expiry date: a warning and a re-attestation prompt, never a block (ADR-016). */
  expired: boolean;
  attestedBy: string | null;
  /** The attesting owner's name when they are a doctor of the practice; otherwise the screen says "the owner". */
  attestedByName: string | null;
  attestedAt: string;
  hasSignature: boolean;
};
export type DoctorList = {
  doctors: Doctor[];
  defaults: { newVisitFee: number; followUpFee: number; currency: 'INR' | 'AED' };
  authorities: Authority[];
};

/** The wizard steps in order (F-09). Schedule only explains what happens next; it collects nothing. */
export const doctorSteps = ['details', 'licence', 'schedule', 'fees', 'access', 'signature'] as const;
export type DoctorStep = (typeof doctorSteps)[number];

const text = (max: number) => z.string().trim().max(max);
const money = z.string().trim().min(1).refine(v => Number.isFinite(Number(v)) && Number(v) >= 0 && Number(v) <= 1_000_000);

export const licenceSchema = z.object({
  authority: z.string().min(1),
  number: z.string().trim().min(1).max(20),
  category: z.string().trim().min(1).max(40),
  expiresOn: z.iso.date(),
  certificateName: text(200),
  attested: z.literal(true),
});
export type LicenceForm = z.infer<typeof licenceSchema>;

export const doctorSchema = licenceSchema.extend({
  fullName: z.string().trim().min(1).max(200),
  email: z.string().trim().max(254).regex(/^[^\s@<>]+@[^\s@<>]+\.[^\s@<>]+$/),
  specialty: z.string().trim().min(1).max(100),
  qualifications: text(200),
  languages: text(200),
  newVisitFee: money,
  followUpFee: money,
  facilityId: z.string().min(1),
});
export type DoctorForm = z.infer<typeof doctorSchema>;

export const emptyLicence = { authority: '', number: '', category: '', expiresOn: '', certificateName: '', attested: false } as unknown as LicenceForm; // attested starts unticked: the owner must tick it
export const emptyDoctor = (facilityId: string, fees: { newVisitFee: number; followUpFee: number } = { newVisitFee: 0, followUpFee: 0 }): DoctorForm => ({
  ...emptyLicence, fullName: '', email: '', specialty: '', qualifications: '', languages: '', facilityId,
  newVisitFee: String(fees.newVisitFee / 100), followUpFee: String(fees.followUpFee / 100),
});

/** Fields each wizard step asks for, so Next validates only its own step. */
export const stepFields: Record<DoctorStep, (keyof DoctorForm)[]> = {
  details: ['fullName', 'email', 'specialty', 'qualifications', 'languages'],
  licence: ['authority', 'number', 'category', 'expiresOn', 'certificateName', 'attested'],
  schedule: [],
  fees: ['newVisitFee', 'followUpFee'],
  access: ['facilityId'],
  signature: [],
};

/** Fees are whole currency units in the form and minor units (x100) on the wire. */
export const toMinor = (major: string) => Math.round(Number(major) * 100);

const blank = (v: string) => (v === '' ? null : v);

export const toAddBody = (f: DoctorForm) => ({
  fullName: f.fullName, email: f.email, specialty: f.specialty, qualifications: blank(f.qualifications), languages: blank(f.languages), facilityId: f.facilityId,
  authority: f.authority, licenceNumber: f.number, category: f.category, expiresOn: f.expiresOn, certificateName: blank(f.certificateName), attested: f.attested,
  newVisitFee: toMinor(f.newVisitFee), followUpFee: toMinor(f.followUpFee),
});

export const toLicenceBody = (f: LicenceForm) => ({ ...f, certificateName: blank(f.certificateName) });

/** The step a server error path ("licence.number", "email", "newVisitFee") belongs to, and the form field under it. */
export function errorTarget(path: string): { step: DoctorStep; field: keyof DoctorForm } | null {
  const field = (path.startsWith('licence.') ? { number: 'number', certificate: 'certificateName' }[path.slice(8)] ?? path.slice(8) : path === 'licenceNumber' ? 'number' : path) as keyof DoctorForm;
  const step = doctorSteps.find(s => stepFields[s].includes(field));
  return step ? { step, field } : null;
}

/** Problem codes from the API ("field:code" for a rejected field, as the shared client reports them). */
export const doctorProblems = ['signature:type', 'signature:too_large'] as const;
export type DoctorProblem = (typeof doctorProblems)[number] | 'failed';
export const doctorProblem = (code: string | undefined): DoctorProblem => ((doctorProblems as readonly string[]).includes(code ?? '') ? (code as DoctorProblem) : 'failed');

export const signatureMaxBytes = 512 * 1024;
export const signatureProblem = (file: { type: string; size: number }): 'type' | 'size' | null =>
  !['image/png', 'image/jpeg'].includes(file.type) ? 'type' : file.size === 0 || file.size > signatureMaxBytes ? 'size' : null;

/** What the screen can say under a field: what the server reported (when we have words for it) or "invalid" for anything else. */
export const fieldProblems = ['invalid', 'required', 'format', 'unknown', 'past', 'range', 'too_long'] as const;
export type FieldProblem = (typeof fieldProblems)[number];
export const fieldProblem = (code: string | undefined): FieldProblem => ((fieldProblems as readonly string[]).includes(code ?? '') ? (code as FieldProblem) : 'invalid');
