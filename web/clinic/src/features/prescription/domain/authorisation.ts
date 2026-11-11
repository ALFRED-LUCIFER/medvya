// Mirrors the MDV-P2-017 records in src/Modules/Prescribing/Medvya.Prescribing.Application/IPrescriptions.cs.
/** `cancellation`: the doctor cancelled this version without a replacement; it no longer prints or opens by link. */
export type RxAuthorisation = {
  versionNo: number; number: string; authorisedBy: string; authorisedAt: string; contentHash: string; lines: number;
  cancellation: { reason: string; cancelledBy: string; cancelledAt: string } | null;
};
/** `authorised` null: never authorised. `changed`: the draft differs from what was authorised, so it does not print until authorised again. */
export type RxAuthorisationState = { authorised: RxAuthorisation | null; changed: boolean };

/** The notes a regulatory class needs on paper (FormularyRules.PrintRequirements). */
export type PrintRequirement = 'warning_schedule_h' | 'warning_schedule_h1' | 'warning_schedule_x' | 'warning_controlled' | 'pharmacist_keeps_copy' | 'duplicate_prescription';
export type RxPrintLine = {
  discontinue: boolean; medicine: string; strength: string | null; form: string | null; sig: Record<string, string>; printRequirements: PrintRequirement[]; reason: string | null;
};
export type RxPrint = {
  authorisation: RxAuthorisation; clinic: string; timeZone: string; doctor: string; qualifications: string | null; licenceNumber: string; licenceAuthority: string;
  patient: { fullName: string; mrn: string; dateOfBirth: string; sexAtBirth: 'Female' | 'Male' | 'Other' };
  allergies: 'NotRecorded' | 'NoKnownAllergies' | 'Listed'; allergySubstances: string[]; lines: RxPrintLine[]; verifyUrl: string; qr: string[]; layoutVersion: number;
};
export type RxLayout = { layoutVersion: number; approvedAt: string | null };
/** What the public QR check answers: validity and what the paper shows, never the patient or the medicines. */
/** The public QR check of an investigation slip or referral letter (MDV-P2-018/019): what it is, when and from whom, how many items; never whose. */
export type PaperVerify = { kind: 'InvestigationSlip' | 'ReferralLetter'; issuedAt: string; timeZone: string; clinic: string; doctor: string; items: number };

export type RxVerify = { status: 'Valid' | 'Replaced' | 'Cancelled'; number: string; versionNo: number; authorisedAt: string; timeZone: string; clinic: string; doctor: string; lines: number };

export type AuthoriseProblem = 'reauth' | 'unresolved_alerts' | 'nothing_to_authorise' | 'no_instructions' | 'no_licence' | 'failed';
const known: readonly string[] = ['unresolved_alerts', 'nothing_to_authorise', 'no_instructions', 'no_licence'];
/** The server's refusal as a problem: 403 reauth_required asks for the step-up; a 409 carries a code; a 400 carries "lines:no_instructions". */
export function authoriseProblem(status: number | undefined, code: string | undefined): AuthoriseProblem {
  if (status === 403 && code === 'reauth_required') return 'reauth';
  const reason = code?.includes(':') ? code.split(':')[1] : code;
  return reason && known.includes(reason) ? (reason as AuthoriseProblem) : 'failed';
}

/** Authorised, unchanged and not cancelled: the prescription that prints and can be cancelled. */
export const isCurrent = (s: RxAuthorisationState | null) => !!s?.authorised && !s.changed && !s.authorised.cancellation;

/** The second print language beside English: none, or one of the pilot languages. Arabic prints right to left. */
export const secondLanguages = ['none', 'hi', 'ta', 'ar'] as const;
export type SecondLanguage = (typeof secondLanguages)[number];

/** Dark modules of the QR code as [x, y], from the server's rows ('1' dark), quiet zone included. */
export const qrCells = (rows: string[]): [number, number][] => rows.flatMap((row, y) => [...row].flatMap((c, x): [number, number][] => (c === '1' ? [[x, y]] : [])));

/** A PDF refusal: 422 is text the server PDF cannot show yet (another script), so the page is printed instead. */
export const pdfProblem = (status: number | undefined) => (status === 422 ? 'unsupported' : 'failed');
