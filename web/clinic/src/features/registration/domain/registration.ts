import { z } from 'zod';
import type { Country, MessageKey } from '@medvya/i18n';
import type { IdentifierScheme, PayerKind, RegistrationStatus, SexAtBirth } from '../../patients/domain/directory';

// Patient registration (P-02): full mode, India (MDV-P1-013) and UAE (MDV-P1-014), and quick mode (MDV-P1-015), with the duplicate
// check (MDV-P1-016). The server (CountryRules) repeats every rule; these only spare the desk a round trip. Messages are i18n keys.

export const sexes: readonly SexAtBirth[] = ['Female', 'Male', 'Other'];
export const isSex = (s: string): s is SexAtBirth => (sexes as readonly string[]).includes(s);

/** Full-mode steps. Family & emergency (MDV-P1-020) and Consents (MDV-P1-019) join with their tickets. */
export type Step = 'identity' | 'contact' | 'payer' | 'review';
export const steps: readonly Step[] = ['identity', 'contact', 'payer', 'review'];

/** The one identifier quick mode asks for: ABHA (optional) in India, Emirates ID (required) in the UAE. */
export const quickIdentifier = {
  IN: { scheme: 'Abha', field: 'abha', required: false },
  AE: { scheme: 'EmiratesId', field: 'emiratesId', required: true },
} as const satisfies Record<Country, { scheme: IdentifierScheme; field: string; required: boolean }>;

/** India "Other ID type": Aadhaar is never more than its last 4 digits. "None" leaves the value unused. */
export const otherIdTypes = ['AadhaarLast4', 'Passport'] as const;
export const otherIdChoices = ['None', ...otherIdTypes] as const;

export const genderIdentities = ['SameAsSexAtBirth', 'Woman', 'Man', 'Transgender', 'NonBinary', 'PreferNotToSay'] as const;

/** BCP 47 codes, named in the UI language by Intl.DisplayNames. */
export const languages: Record<Country, readonly string[]> = {
  IN: ['ml', 'ta', 'hi', 'en', 'kn', 'te', 'bn', 'mr', 'gu', 'pa', 'or', 'ur'],
  AE: ['ar', 'en', 'hi', 'ur', 'ml', 'tl', 'bn', 'fa', 'fr'],
};

/** ISO 3166-1 alpha-2, named in the UI language by Intl.DisplayNames. */
export const nationalities: readonly string[] = [
  'AE', 'IN', 'PK', 'PH', 'EG', 'BD', 'LK', 'NP', 'SA', 'OM', 'JO', 'LB', 'SY', 'SD', 'YE', 'IR', 'GB', 'US', 'CA', 'AU', 'FR', 'DE', 'RU', 'CN', 'NG', 'KE', 'ET',
];

/** The 28 states and 8 union territories, stored by official English name (IndiaRules.StatesAndUnionTerritories). */
export const indiaStates = [
  'Andhra Pradesh', 'Arunachal Pradesh', 'Assam', 'Bihar', 'Chhattisgarh', 'Goa', 'Gujarat', 'Haryana', 'Himachal Pradesh', 'Jharkhand',
  'Karnataka', 'Kerala', 'Madhya Pradesh', 'Maharashtra', 'Manipur', 'Meghalaya', 'Mizoram', 'Nagaland', 'Odisha', 'Punjab', 'Rajasthan',
  'Sikkim', 'Tamil Nadu', 'Telangana', 'Tripura', 'Uttar Pradesh', 'Uttarakhand', 'West Bengal',
  'Andaman and Nicobar Islands', 'Chandigarh', 'Dadra and Nagar Haveli and Daman and Diu', 'Delhi', 'Jammu and Kashmir', 'Ladakh',
  'Lakshadweep', 'Puducherry',
] as const;

/** Emirate codes (UaeRules); the emirate selects the health information exchange shown in consents (no connection in P1). */
export const emirates = ['AUH', 'DXB', 'SHJ', 'AJM', 'UAQ', 'RAK', 'FUJ'] as const;
export type Hie = 'Malaffi' | 'NABIDH' | 'Riayati';
export const hieFor = (emirate: string): Hie | null =>
  emirate === 'AUH' ? 'Malaffi' : emirate === 'DXB' ? 'NABIDH' : (emirates as readonly string[]).includes(emirate) ? 'Riayati' : null;

export const payerKinds: Record<Country, readonly PayerKind[]> = {
  IN: ['SelfPay', 'Corporate', 'PrivateInsurance', 'GovernmentScheme'],
  AE: ['PrivateInsurance', 'SelfPay', 'Corporate'],
};

export type PayerField = 'payerName' | 'plan' | 'memberNumber' | 'validTo' | 'network';

/** Fields asked for one payment type; only these are sent, so switching the type never sends another type's leftovers. */
export function payerFields(country: Country, kind: PayerKind | ''): readonly PayerField[] {
  switch (kind) {
    case 'Corporate': return ['payerName', 'memberNumber'];
    case 'GovernmentScheme': return ['payerName', 'memberNumber', 'validTo'];
    // India: the TPA administers the policy and is recorded as its network.
    case 'PrivateInsurance': return country === 'IN' ? ['payerName', 'network', 'memberNumber', 'validTo'] : ['payerName', 'plan', 'memberNumber', 'validTo', 'network'];
    default: return [];
  }
}

/** Label key for a payer field: the same column means insurer, employer or scheme depending on the payment type (keys tested). */
export function payerLabel(country: Country, kind: PayerKind | '', field: PayerField): MessageKey {
  return (kind === 'PrivateInsurance' ? `registration.payer.${field}.insurance.${country}` : `registration.payer.${field}.${kind}`) as MessageKey;
}

/** Translation key of a stored state/UT or emirate, or null for a value outside the list. */
export function regionKey(country: Country, region: string): MessageKey | null {
  if (country === 'IN') return (indiaStates as readonly string[]).includes(region) ? `registration.state.${region as (typeof indiaStates)[number]}` : null;
  return (emirates as readonly string[]).includes(region) ? `registration.emirate.${region as (typeof emirates)[number]}` : null;
}

const abha = /^\d{2}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}$/; // 14 digits, shown XX-XXXX-XXXX-XXXX
const abhaAddress = /^[a-z0-9][a-z0-9._]{2,49}@(abdm|sbx)$/i;
const emiratesId = /^784-?\d{4}-?\d{7}-?\d$/;               // 784-YYYY-NNNNNNN-N
const passport = /^[A-Z0-9]{6,20}$/i;
const visa = /^[A-Z0-9/-]{5,30}$/i;
const lastFour = /^\d{4}$/;
const fullAadhaar = /^\d{4}[\s-]?\d{4}[\s-]?\d{4}$/;
const pin = /^\d{6}$/;
const makani = /^\d{10}$/;
const poBox = /^\d{1,10}$/;
const memberNumber = /^[A-Z0-9/-]{1,50}$/i;
const email = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const isoDate = /^\d{4}-\d{2}-\d{2}$/;

const text = (max = 200) => z.string().max(max, 'too_long');

const shape = z.object({
  fullName: z.string().trim().min(1, 'required').max(200, 'too_long'),
  arabicName: text(),
  /** India may record an age instead (the date is then marked estimated); the UAE needs the date. */
  ageInstead: z.boolean(),
  dateOfBirth: z.string(),
  ageYears: z.string(),
  sexAtBirth: z.string(),
  genderIdentity: z.string(),
  preferredLanguage: z.string(),
  nationality: z.string(),
  abha: z.string(),
  abhaAddress: z.string(),
  otherIdType: z.string(),
  otherIdValue: z.string(),
  emiratesId: z.string(),
  emiratesIdExpiry: z.string(),
  passport: z.string(),
  visa: z.string(),
  mobile: z.string().trim().min(1, 'required').max(25, 'format'),
  alternatePhone: z.string().max(25, 'format'),
  email: z.string().max(254, 'too_long'),
  line1: text(), line2: text(), city: text(), district: text(), region: text(), postalCode: text(), makani: text(), poBox: text(),
  payerKind: z.string(),
  payerName: text(), plan: text(100), memberNumber: text(50), validTo: z.string(), network: text(100),
});

export type RegistrationForm = z.input<typeof shape>;
export type FormField = keyof RegistrationForm;

export const emptyForm: RegistrationForm = {
  fullName: '', arabicName: '', ageInstead: false, dateOfBirth: '', ageYears: '', sexAtBirth: '', genderIdentity: '', preferredLanguage: '', nationality: '',
  abha: '', abhaAddress: '', otherIdType: '', otherIdValue: '', emiratesId: '', emiratesIdExpiry: '', passport: '', visa: '',
  mobile: '', alternatePhone: '', email: '', line1: '', line2: '', city: '', district: '', region: '', postalCode: '', makani: '', poBox: '',
  payerKind: '', payerName: '', plan: '', memberNumber: '', validTo: '', network: '',
};

/** Fields each full-mode step owns; "Continue" validates only these. */
export const stepFields: Record<Exclude<Step, 'review'>, readonly FormField[]> = {
  identity: ['fullName', 'arabicName', 'dateOfBirth', 'ageYears', 'sexAtBirth', 'genderIdentity', 'preferredLanguage', 'nationality', 'abha', 'abhaAddress',
    'otherIdType', 'otherIdValue', 'emiratesId', 'emiratesIdExpiry', 'passport', 'visa'],
  contact: ['mobile', 'alternatePhone', 'email', 'line1', 'line2', 'city', 'district', 'region', 'postalCode', 'makani', 'poBox'],
  payer: ['payerKind', 'payerName', 'plan', 'memberNumber', 'validTo', 'network'],
};

/** The step that shows a field (for jumping to the first error). Quick mode shows mobile on its single screen. */
export const stepOf = (field: FormField): Step =>
  (Object.keys(stepFields) as Exclude<Step, 'review'>[]).find(s => stepFields[s].includes(field)) ?? 'identity';

const filled = (s: string) => s.trim() !== '';
const addressFields: Record<Country, readonly FormField[]> = {
  IN: ['line1', 'line2', 'city', 'district', 'region', 'postalCode'],
  AE: ['line1', 'line2', 'region', 'makani', 'poBox'],
};
export const hasAddress = (v: RegistrationForm, country: Country) => addressFields[country].some(f => filled(v[f] as string));

export function registrationSchema(country: Country, quick: boolean) {
  return shape.superRefine((v, ctx) => {
    const issue = (path: FormField, message: string) => ctx.addIssue({ code: 'custom', path: [path], message });

    if (country === 'IN' && v.ageInstead) {
      const age = Number(v.ageYears);
      if (!filled(v.ageYears)) issue('ageYears', 'required');
      else if (!Number.isInteger(age) || age < 0 || age > 130) issue('ageYears', 'out_of_range');
    } else if (!isoDate.test(v.dateOfBirth)) issue('dateOfBirth', 'required');
    if (!isSex(v.sexAtBirth)) issue('sexAtBirth', 'required');

    if (country === 'IN') {
      if (filled(v.abha) && !abha.test(v.abha.trim())) issue('abha', 'format.Abha');
    } else {
      const eid = v.emiratesId.trim();
      if (eid !== '' && !emiratesId.test(eid)) issue('emiratesId', 'format.EmiratesId');
      else if (eid === '' && quick) issue('emiratesId', 'required');
      else if (eid === '' && !filled(v.passport)) issue('emiratesId', 'emirates_id_or_passport_required');
    }
    if (quick) return;

    if (country === 'IN') {
      if (filled(v.abhaAddress) && !abhaAddress.test(v.abhaAddress.trim())) issue('abhaAddress', 'format.AbhaAddress');
      const other = v.otherIdValue.trim();
      if ((otherIdTypes as readonly string[]).includes(v.otherIdType) && other === '') issue('otherIdValue', 'required');
      else if (v.otherIdType === 'AadhaarLast4' && fullAadhaar.test(other)) issue('otherIdValue', 'aadhaar_full_number_not_allowed');
      else if (v.otherIdType === 'AadhaarLast4' && !lastFour.test(other)) issue('otherIdValue', 'format.AadhaarLast4');
      else if (v.otherIdType === 'Passport' && !passport.test(other)) issue('otherIdValue', 'format.Passport');
    } else {
      if (filled(v.passport) && !passport.test(v.passport.trim())) issue('passport', 'format.Passport');
      if (filled(v.visa) && !visa.test(v.visa.trim())) issue('visa', 'format');
    }

    if (filled(v.email) && !email.test(v.email.trim())) issue('email', 'format.email');
    checkAddressAndPayer(v, country, issue);
  });
}

type Issue = (path: FormField, message: string) => void;

/** Address (once any part is entered) and payer (once a type is chosen): shared by full registration and completion. */
function checkAddressAndPayer(v: RegistrationForm, country: Country, issue: Issue) {
  if (hasAddress(v, country)) {
    if (!filled(v.line1)) issue('line1', 'required');
    if (country === 'IN') {
      if (!filled(v.city)) issue('city', 'required');
      if (!(indiaStates as readonly string[]).includes(v.region)) issue('region', 'required');
      if (!pin.test(v.postalCode.trim())) issue('postalCode', 'format.pin');
    } else {
      if (hieFor(v.region) === null) issue('region', 'emirate_required');
      if (filled(v.makani) && !makani.test(v.makani.trim())) issue('makani', 'format.makani');
      if (filled(v.poBox) && !poBox.test(v.poBox.trim())) issue('poBox', 'format.poBox');
    }
  }
  if (v.payerKind !== '') {
    const kind = v.payerKind as PayerKind;
    if (!payerKinds[country].includes(kind)) issue('payerKind', 'required');
    const asked = payerFields(country, kind);
    if (asked.includes('payerName') && !filled(v.payerName)) issue('payerName', 'required');
    if (kind === 'PrivateInsurance' && !filled(v.memberNumber)) issue('memberNumber', 'required');
    else if (asked.includes('memberNumber') && filled(v.memberNumber) && !memberNumber.test(v.memberNumber.trim())) issue('memberNumber', 'format');
  }
}

/**
 * Completing a quick registration (MDV-P1-015): the address, the payer or both. With neither, the address is asked for
 * ("nothing_to_complete" on line 1), since a save that adds nothing would close nothing.
 */
export function completionSchema(country: Country) {
  return shape.pick({
    line1: true, line2: true, city: true, district: true, region: true, postalCode: true, makani: true, poBox: true,
    payerKind: true, payerName: true, plan: true, memberNumber: true, validTo: true, network: true,
  }).superRefine((v, ctx) => {
    const issue: Issue = (path, message) => ctx.addIssue({ code: 'custom', path: [path], message });
    const form = { ...emptyForm, ...v };
    if (!hasAddress(form, country) && v.payerKind === '') issue('line1', 'nothing_to_complete');
    checkAddressAndPayer(form, country, issue);
  });
}

/** Today in the browser's calendar as yyyy-mm-dd (the desk sits at the facility). */
export const isoDay = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

/** An Emirates ID past its expiry warns but never blocks registration or care. */
export const eidExpired = (expiry: string, today: string) => isoDate.test(expiry) && expiry < today;

/** Whole years on <today> (yyyy-mm-dd strings); null until the date is complete. */
export function ageOn(dateOfBirth: string, today: string): number | null {
  if (!isoDate.test(dateOfBirth) || dateOfBirth > today) return null;
  const [y, m, d] = dateOfBirth.split('-').map(Number) as [number, number, number];
  const [ty, tm, td] = today.split('-').map(Number) as [number, number, number];
  return ty - y - (tm < m || (tm === m && td < d) ? 1 : 0);
}

export type IdentifierInput = { scheme: IdentifierScheme; value: string; expiresOn?: string | null };

/** Identifiers as entered (trimmed), in a fixed order; quick mode has only its one identifier. */
export function identifierInputs(v: RegistrationForm, country: Country, quick: boolean): IdentifierInput[] {
  const ids: IdentifierInput[] = [];
  const add = (scheme: IdentifierScheme, value: string, expiresOn?: string) => {
    if (filled(value)) ids.push({ scheme, value: value.trim(), ...(expiresOn !== undefined ? { expiresOn: isoDate.test(expiresOn) ? expiresOn : null } : {}) });
  };
  if (country === 'IN') {
    add('Abha', v.abha);
    if (!quick) {
      add('AbhaAddress', v.abhaAddress);
      if ((otherIdTypes as readonly string[]).includes(v.otherIdType)) add(v.otherIdType as IdentifierScheme, v.otherIdValue);
    }
  } else {
    add('EmiratesId', v.emiratesId, quick ? undefined : v.emiratesIdExpiry);
    if (!quick) { add('Passport', v.passport); add('Visa', v.visa); }
  }
  return ids;
}

/** The form field that holds an identifier scheme (server errors arrive as identifiers[i]). */
export function identifierField(scheme: IdentifierScheme, country: Country): FormField {
  switch (scheme) {
    case 'Abha': return 'abha';
    case 'AbhaAddress': return 'abhaAddress';
    case 'AadhaarLast4': return 'otherIdValue';
    case 'Passport': return country === 'IN' ? 'otherIdValue' : 'passport';
    case 'EmiratesId': return 'emiratesId';
    case 'Visa': return 'visa';
  }
}

const lookup: Partial<Record<IdentifierScheme, RegExp>> = { Abha: abha, AbhaAddress: abhaAddress, EmiratesId: emiratesId, Passport: passport };

export type DuplicateQuery = { fullName: string; dateOfBirth: string | null; identifiers: { scheme: IdentifierScheme; value: string }[] };

/**
 * What to compare while typing. A stated age is not a date of birth, so it is never sent: only a complete date or identifier is.
 * Only identifiers matched exactly (ABHA number and address, Emirates ID, passport) go, and only when complete. Never the phone.
 * Null = nothing to compare yet (fewer than two letters of name and no identifier).
 */
export function duplicateQuery(v: RegistrationForm, country: Country, quick: boolean): DuplicateQuery | null {
  const fullName = v.fullName.trim();
  const dateOfBirth = !(country === 'IN' && v.ageInstead) && isoDate.test(v.dateOfBirth) ? v.dateOfBirth : null;
  const identifiers = identifierInputs(v, country, quick)
    .filter(i => lookup[i.scheme]?.test(i.value))
    .map(({ scheme, value }) => ({ scheme, value }));
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

export type AddressInput = {
  line1: string; line2: string | null; city: string | null; district: string | null; region: string | null;
  postalCode: string | null; makani: string | null; poBox: string | null;
};
export type PayerInput = {
  kind: PayerKind; payerName: string | null; plan: string | null; memberNumber: string | null; validTo: string | null; network: string | null;
};

export type RegisterRequest = {
  quick: boolean; fullName: string; arabicName: string | null; dateOfBirth: string | null; ageYears: number | null; sexAtBirth: SexAtBirth;
  genderIdentity: string | null; preferredLanguage: string | null; nationality: string | null;
  mobile: string; alternatePhone: string | null; email: string | null;
  identifiers: IdentifierInput[]; address: AddressInput | null; payer: PayerInput | null; duplicates: DuplicateResolution | null;
};

const orNull = (s: string) => (filled(s) ? s.trim() : null);

/** The request for the fields this mode and country show; anything else on the form is not sent. */
export function toRegisterRequest(v: RegistrationForm, country: Country, quick: boolean, duplicates: DuplicateResolution | null): RegisterRequest {
  const ageInstead = country === 'IN' && v.ageInstead;
  const full = !quick;
  return {
    quick, fullName: v.fullName.trim(), arabicName: full && country === 'AE' ? orNull(v.arabicName) : null,
    dateOfBirth: ageInstead ? null : v.dateOfBirth, ageYears: ageInstead ? Number(v.ageYears) : null,
    sexAtBirth: v.sexAtBirth as SexAtBirth,
    genderIdentity: full && country === 'IN' ? orNull(v.genderIdentity) : null,
    preferredLanguage: full ? orNull(v.preferredLanguage) : null,
    nationality: full && country === 'AE' ? orNull(v.nationality) : null,
    mobile: v.mobile.trim(), alternatePhone: full ? orNull(v.alternatePhone) : null, email: full ? orNull(v.email) : null,
    identifiers: identifierInputs(v, country, quick),
    address: full ? addressInput(v, country) : null,
    payer: full ? payerInput(v, country) : null,
    duplicates,
  };
}

/** The address as entered, in this country's shape; null when no address field is filled. */
function addressInput(v: RegistrationForm, country: Country): AddressInput | null {
  if (!hasAddress(v, country)) return null;
  const shown = (f: FormField) => (addressFields[country].includes(f) ? orNull(v[f] as string) : null);
  return {
    line1: v.line1.trim(), line2: orNull(v.line2), city: shown('city'), district: shown('district'), region: orNull(v.region),
    postalCode: shown('postalCode'), makani: shown('makani'), poBox: shown('poBox'),
  };
}

/** The payer with only the fields its type asks for; null when no type is chosen. */
function payerInput(v: RegistrationForm, country: Country): PayerInput | null {
  const kind = v.payerKind as PayerKind | '';
  if (kind === '') return null;
  const asked = payerFields(country, kind);
  const value = (f: PayerField) => (asked.includes(f) ? orNull(v[f]) : null);
  return {
    kind, payerName: value('payerName'), plan: value('plan'), memberNumber: value('memberNumber'),
    validTo: asked.includes('validTo') && isoDate.test(v.validTo) ? v.validTo : null, network: value('network'),
  };
}

export type CompletionRequest = { address: AddressInput | null; payer: PayerInput | null };

/** Only the address and payer go; the form's other fields (the completion form has none) are never sent. */
export const toCompletionRequest = (v: Partial<RegistrationForm>, country: Country): CompletionRequest =>
  ({ address: addressInput({ ...emptyForm, ...v }, country), payer: payerInput({ ...emptyForm, ...v }, country) });

export type RegistrationResult = { patientId: string; mrn: string; registeredAt: string; status: RegistrationStatus; warnings: string[] };

/** Error codes with their own message (client schema and CountryRules); any other code shows the generic one. */
export const knownErrors = [
  'required', 'too_long', 'format', 'out_of_range', 'scheme_not_allowed', 'duplicate_scheme', 'not_allowed', 'not_applicable',
  'emirates_id_required', 'emirates_id_or_passport_required', 'emirate_required', 'aadhaar_full_number_not_allowed',
  'format.Abha', 'format.AbhaAddress', 'format.AadhaarLast4', 'format.EmiratesId', 'format.Passport', 'format.pin', 'format.makani', 'format.poBox',
  'format.email', 'nothing_to_complete',
] as const;
export type KnownError = (typeof knownErrors)[number] | 'generic';
export const errorCode = (code: string | undefined): KnownError =>
  (knownErrors as readonly string[]).includes(code ?? '') ? (code as KnownError) : 'generic';

const direct: readonly FormField[] = [
  'fullName', 'arabicName', 'mobile', 'alternatePhone', 'email', 'dateOfBirth', 'ageYears', 'sexAtBirth', 'genderIdentity', 'preferredLanguage', 'nationality',
];
const payerPaths: Record<string, FormField> = {
  'payer.kind': 'payerKind', 'payer.payerName': 'payerName', 'payer.plan': 'plan', 'payer.memberNumber': 'memberNumber', 'payer.validTo': 'validTo', 'payer.network': 'network',
};

/** Server field paths (CountryRules) → form fields; anything else is shown as a form-level error. */
export function formField(serverField: string, request: Pick<RegisterRequest, 'identifiers'>, country: Country): FormField | null {
  if ((direct as readonly string[]).includes(serverField)) return serverField as FormField;
  if (serverField.startsWith('address.')) {
    const f = serverField.slice('address.'.length) as FormField;
    return (addressFields.IN.includes(f) || addressFields.AE.includes(f)) ? f : null;
  }
  if (serverField in payerPaths) return payerPaths[serverField]!;
  const index = /^identifiers\[(\d+)\]$/.exec(serverField)?.[1];
  if (index !== undefined) {
    const id = request.identifiers[Number(index)];
    return id ? identifierField(id.scheme, country) : null;
  }
  return serverField === 'identifiers' ? quickIdentifier[country].field : null;
}

const formatCode: Partial<Record<FormField, KnownError>> = {
  abha: 'format.Abha', abhaAddress: 'format.AbhaAddress', emiratesId: 'format.EmiratesId', passport: 'format.Passport', postalCode: 'format.pin',
  makani: 'format.makani', poBox: 'format.poBox', email: 'format.email',
};

/** A server code shown under a field: a format error gets the field's own wording (e.g. "PIN code must be 6 digits"). */
export const messageFor = (field: FormField, code: string): string => (code === 'format' && formatCode[field]) || code;

/** Side panel "Data map · this step writes" (P-02): which records a step fills. Entity names are the data model's. */
export const dataMap = {
  quick: [{ entity: 'PATIENT', key: 'quick' }, { entity: 'PATIENT_IDENTIFIER', key: 'identifier' }],
  identity: [{ entity: 'PATIENT', key: 'identity' }, { entity: 'PATIENT_IDENTIFIER', key: 'identifier' }],
  contact: [{ entity: 'PATIENT', key: 'contact' }],
  payer: [{ entity: 'PAYER_COVERAGE', key: 'payer' }],
  review: [{ entity: 'AUDIT_EVENT', key: 'audit' }],
} as const satisfies Record<Step | 'quick', readonly { entity: string; key: string }[]>;

/** Side panel rules, per country (keys under registration.rules.). */
export const rules = {
  IN: ['minimumIN', 'phone', 'abha', 'aadhaar', 'clinical'],
  AE: ['minimumAE', 'expired', 'arabic', 'hie', 'residency'],
} as const satisfies Record<Country, readonly string[]>;
