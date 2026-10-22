// Mirrors src/Modules/Patients (Medvya.Patients.Application / Medvya.Patients.Api); the server is the authority.
export type DirectoryFilter = 'All' | 'Incomplete' | 'Recent';
export type SearchKind = 'Directory' | 'Name' | 'Phone' | 'Mrn' | 'Identifier';
export type SexAtBirth = 'Female' | 'Male' | 'Other';
export type RegistrationStatus = 'Complete' | 'Incomplete';
export type IdentifierScheme = 'Abha' | 'AbhaAddress' | 'AadhaarLast4' | 'EmiratesId' | 'Passport' | 'Visa';
export type PayerKind = 'SelfPay' | 'Corporate' | 'PrivateInsurance' | 'GovernmentScheme';

/** A privacy-safe result row: initials, age, sex and the MRN tail only, until the record is opened (audited). */
export type PatientSummary = {
  id: string; initials: string; ageYears: number; ageEstimated: boolean; sexAtBirth: SexAtBirth; mrnTail: string; status: RegistrationStatus;
};

export type SearchResult = { matchedOn: SearchKind; items: PatientSummary[]; page: number; pageSize: number; total: number };

/** Search text goes in the POST body only: names, phones and IDs are PHI and never belong in a URL. */
export type SearchRequest = { query: string | null; filter: DirectoryFilter; page: number };

export type PatientAddress = {
  line1: string; line2?: string | null; city?: string | null; district?: string | null; region?: string | null;
  postalCode?: string | null; makani?: string | null; poBox?: string | null;
};

export type PatientDetails = {
  id: string; mrn: string; fullName: string; arabicName: string | null; dateOfBirth: string; dateOfBirthEstimated: boolean; ageYears: number;
  sexAtBirth: SexAtBirth; genderIdentity: string | null; preferredLanguage: string | null; nationality: string | null;
  mobile: string; alternatePhone: string | null; email: string | null; address: PatientAddress | null; status: RegistrationStatus;
  registeredAt: string;
  identifiers: { scheme: IdentifierScheme; value: string; expiresOn: string | null }[];
  payer: { kind: PayerKind; payerName: string | null; plan: string | null; memberNumber: string | null; validTo: string | null; network: string | null } | null;
};

export const directoryFilters: readonly DirectoryFilter[] = ['All', 'Incomplete', 'Recent'];

/** Error codes the server sends for an unreadable search (CountryRules.ParseSearch); anything else is a generic failure. */
export const searchErrorCodes = ['full_number_required', 'too_short', 'too_long'] as const;
export type SearchErrorCode = (typeof searchErrorCodes)[number];

export const asSearchError = (code: string | undefined): SearchErrorCode | null =>
  (searchErrorCodes as readonly string[]).includes(code ?? '') ? (code as SearchErrorCode) : null;

/** Blank search = the directory list. The server applies the same trim and a 100-character limit. */
export const toQuery = (text: string): string | null => (text.trim() === '' ? null : text.trim());

export const pageCount = (r: Pick<SearchResult, 'total' | 'pageSize'>) => Math.max(1, Math.ceil(r.total / r.pageSize));

/** Address lines in display order, empty parts dropped. */
export const addressLines = (a: PatientAddress): string[] =>
  [a.line1, a.line2, a.city, a.district, a.region, a.postalCode, a.makani, a.poBox].filter((p): p is string => !!p);

