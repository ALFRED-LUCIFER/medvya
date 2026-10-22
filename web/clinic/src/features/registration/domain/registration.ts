import { z } from 'zod';
import type { Country } from '@medvya/i18n';
import type { IdentifierScheme, RegistrationStatus, SexAtBirth } from '../../patients/domain/directory';

// Quick registration (P-02 "Quick (busy desk)", MDV-P1-015) with the duplicate check (MDV-P1-016). The server repeats every rule;
// these only spare the desk a round trip. Error messages are i18n keys.

export const sexes: readonly SexAtBirth[] = ['Female', 'Male', 'Other'];

/** The one identifier quick mode asks for: ABHA (optional) in India, Emirates ID (required) in the UAE. */
export const quickIdentifier: Record<Country, { scheme: 'Abha' | 'EmiratesId'; required: boolean }> = {
  IN: { scheme: 'Abha', required: false },
  AE: { scheme: 'EmiratesId', required: true },
};

const abha = /^\d{2}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}$/; // 14 digits, shown XX-XXXX-XXXX-XXXX
const emiratesId = /^784-?\d{4}-?\d{7}-?\d$/;              // 784-YYYY-NNNNNNN-N
const isoDate = /^\d{4}-\d{2}-\d{2}$/;

export function quickSchema(country: Country) {
  const id = quickIdentifier[country];
  return z.object({
    fullName: z.string().trim().min(1, 'required').max(200, 'too_long'),
    mobile: z.string().trim().min(1, 'required').max(25, 'format'),
    /** India may record an age instead (the date is then marked estimated); the UAE needs the date. */
    ageInstead: z.boolean(),
    dateOfBirth: z.string(),
    ageYears: z.string(),
    sexAtBirth: z.string().refine(s => (sexes as readonly string[]).includes(s), 'required'),
    identifier: z.string().trim(),
  }).superRefine((v, ctx) => {
    if (country === 'IN' && v.ageInstead) {
      const age = Number(v.ageYears);
      if (v.ageYears.trim() === '') ctx.addIssue({ code: 'custom', path: ['ageYears'], message: 'required' });
      else if (!Number.isInteger(age) || age < 0 || age > 130) ctx.addIssue({ code: 'custom', path: ['ageYears'], message: 'out_of_range' });
    } else if (!isoDate.test(v.dateOfBirth)) ctx.addIssue({ code: 'custom', path: ['dateOfBirth'], message: 'required' });
    if (v.identifier === '') {
      if (id.required) ctx.addIssue({ code: 'custom', path: ['identifier'], message: 'required' });
    } else if (!(id.scheme === 'EmiratesId' ? emiratesId : abha).test(v.identifier))
      ctx.addIssue({ code: 'custom', path: ['identifier'], message: `format.${id.scheme}` });
  });
}

export type QuickForm = z.input<ReturnType<typeof quickSchema>>;

export const emptyQuickForm: QuickForm = { fullName: '', mobile: '', ageInstead: false, dateOfBirth: '', ageYears: '', sexAtBirth: '', identifier: '' };

export type DuplicateQuery = { fullName: string; dateOfBirth: string | null; identifiers: { scheme: IdentifierScheme; value: string }[] };

/**
 * What to compare while typing. A stated age is not a date of birth, so it is never sent: only a complete date or identifier is.
 * Null = nothing to compare yet (fewer than two letters of name and no identifier).
 */
export function duplicateQuery(v: QuickForm, country: Country): DuplicateQuery | null {
  const fullName = v.fullName.trim();
  const dateOfBirth = !v.ageInstead && isoDate.test(v.dateOfBirth) ? v.dateOfBirth : null;
  const scheme = quickIdentifier[country].scheme;
  const value = v.identifier.trim();
  const complete = value !== '' && (scheme === 'EmiratesId' ? emiratesId : abha).test(value);
  const identifiers = complete ? [{ scheme, value }] : [];
  const letters = [...fullName].filter(c => /\p{L}/u.test(c)).length;
  if (identifiers.length === 0 && (letters < 2 || !dateOfBirth)) return null;
  return { fullName, dateOfBirth, identifiers };
}

export type MaskedIdentifier = { scheme: IdentifierScheme; lastFour: string };
export type DuplicateCandidate = {
  patientId: string; initials: string; ageYears: number; sexAtBirth: SexAtBirth; mrnTail: string;
  matchedOnNameAndDateOfBirth: boolean; matchedIdentifiers: IdentifierScheme[]; identifiers: MaskedIdentifier[];
};

export type DuplicateDecision = 'NotSamePerson' | 'MergeReview';
export type DuplicateResolution = { decision: DuplicateDecision; candidateIds: string[] };

/** Save is allowed only when the decision covers every possible match now shown (the server checks the same). */
export const resolves = (r: DuplicateResolution | null, candidates: DuplicateCandidate[]) =>
  candidates.length === 0 || (!!r && candidates.every(c => r.candidateIds.includes(c.patientId)));

export type RegisterRequest = {
  quick: true; fullName: string; mobile: string; dateOfBirth: string | null; ageYears: number | null; sexAtBirth: SexAtBirth;
  identifiers: { scheme: IdentifierScheme; value: string }[]; duplicates: DuplicateResolution | null;
};

export function toRegisterRequest(v: QuickForm, country: Country, duplicates: DuplicateResolution | null): RegisterRequest {
  const ageInstead = country === 'IN' && v.ageInstead;
  return {
    quick: true, fullName: v.fullName.trim(), mobile: v.mobile.trim(),
    dateOfBirth: ageInstead ? null : v.dateOfBirth, ageYears: ageInstead ? Number(v.ageYears) : null,
    sexAtBirth: v.sexAtBirth as SexAtBirth,
    identifiers: v.identifier.trim() === '' ? [] : [{ scheme: quickIdentifier[country].scheme, value: v.identifier.trim() }],
    duplicates,
  };
}

export type RegistrationResult = { patientId: string; mrn: string; registeredAt: string; status: RegistrationStatus; warnings: string[] };

/** Error codes with their own message (client schema and CountryRules); any other code shows the generic one. */
export const knownErrors = [
  'required', 'too_long', 'format', 'out_of_range', 'format.Abha', 'format.EmiratesId', 'scheme_not_allowed', 'duplicate_scheme',
  'emirates_id_required',
] as const;
export type KnownError = (typeof knownErrors)[number] | 'generic';
export const errorCode = (code: string | undefined): KnownError =>
  (knownErrors as readonly string[]).includes(code ?? '') ? (code as KnownError) : 'generic';

/** Server field paths (CountryRules) → form fields; anything else is shown as a form-level error. */
export function formField(serverField: string): keyof QuickForm | null {
  if (serverField === 'fullName' || serverField === 'mobile' || serverField === 'dateOfBirth' || serverField === 'ageYears' || serverField === 'sexAtBirth') return serverField;
  return serverField.startsWith('identifiers') ? 'identifier' : null;
}
