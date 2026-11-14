import type { MessageKey } from '@medvya/i18n';

// Mirrors src/Modules/Patients (Medvya.Patients.Application / Medvya.Patients.Api); the server is the authority.
export type DirectoryFilter = 'All' | 'Incomplete' | 'Recent';
export type SearchKind = 'Directory' | 'Name' | 'Phone' | 'Mrn' | 'Identifier' | 'Number';
export type SexAtBirth = 'Female' | 'Male' | 'Other';
export type RegistrationStatus = 'Complete' | 'Incomplete';
export type IdentifierScheme = 'Abha' | 'AbhaAddress' | 'AadhaarLast4' | 'EmiratesId' | 'Passport' | 'Visa';
export type PayerKind = 'SelfPay' | 'Corporate' | 'PrivateInsurance' | 'GovernmentScheme';

/** Directory table columns; the label of each is the i18n key `patients.col.<key>`.
 * shortcut: no column sort, since the server pages results by match relevance; add server-side sort if desks ask to order the whole list. */
export const directoryColumns = ['patient', 'age', 'sex', 'mrn', 'phone', 'lastVisit', 'status'] as const;
export type DirectoryColumn = (typeof directoryColumns)[number];

export type DuplicateMark = 'MainRecord' | 'PossibleDuplicate';

/**
 * A result row for staff who register and book (MDV-AUDIT-1100, MDV-P1-101): the full name, last visit and duplicate mark tell two people apart;
 * the phone and MRN show their last four only. No date of birth: a list is read at a shared desk (decision 15). The rest needs the record opened (audited).
 */
export type PatientSummary = {
  id: string; initials: string; fullName: string; ageYears: number; ageEstimated: boolean; sexAtBirth: SexAtBirth; mrnTail: string; mobileTail: string; status: RegistrationStatus;
  /** Calendar date (yyyy-mm-dd); none when the patient never attended. */
  lastVisitOn: string | null;
  /** Set while a merge request is open: the record that would be merged away, or the older one that would stay. */
  duplicate: DuplicateMark | null;
};

/**
 * The parts of a row's spoken name that tell two same-name people apart (MDV-P1-101), read after the phone as the row shows them: the last visit and
 * the duplicate mark. A spoken name must hold the visible words in their order (axe label-content-name-mismatch).
 */
export function spokenExtras(row: Pick<PatientSummary, 'lastVisitOn' | 'duplicate'>,
  t: (key: MessageKey, vars?: Record<string, string | number>) => string, formatDate: (date: string) => string): { more: string } {
  const extra = (text: string) => t('patients.row.extra', { text });
  return {
    more: extra(row.lastVisitOn ? t('patients.row.lastVisit', { date: formatDate(row.lastVisitOn) }) : t('patients.row.noVisit'))
      + (row.duplicate ? extra(t(`patients.duplicate.${row.duplicate}`)) : ''),
  };
}

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
  /** Calendar date (yyyy-mm-dd) in the facility's country time zone. */
  registeredOn: string;
  /** National and insurance numbers come masked to their last four characters; the full values need the audited reveal. */
  identifiers: { scheme: IdentifierScheme; lastFour: string; expiresOn: string | null }[];
  payer: { kind: PayerKind; payerName: string | null; plan: string | null; memberNumberLastFour: string | null; validTo: string | null; network: string | null } | null;
  /** Names and numbers to call (MDV-P1-020); never portal users. */
  emergencyContact: ContactView | null;
  guardian: ContactView | null;
  /** The row version this was read at; a save sends it back and is refused (409 "changed") if the record moved on (#889). */
  version: number;
};

export type Relationship = 'Spouse' | 'Parent' | 'Child' | 'Sibling' | 'Friend' | 'Sponsor' | 'Other' | 'Mother' | 'Father' | 'Grandparent';
export type ContactView = { name: string; relationship: Relationship; phone: string };

/** What the desk's completion task still waits for (MDV-P1-015/020); the server sets the status from the same three. */
export type Missing = 'address' | 'payer' | 'emergencyContact';
export const missingParts = (p: Record<Missing, unknown>): Missing[] =>
  [!p.address && 'address' as const, !p.payer && 'payer' as const, !p.emergencyContact && 'emergencyContact' as const].filter((m): m is Missing => !!m);

export type RevealedIdentifiers = { identifiers: { scheme: IdentifierScheme; value: string }[]; memberNumber: string | null };

export const directoryFilters: readonly DirectoryFilter[] = ['All', 'Incomplete', 'Recent'];

/** Error codes the server sends for an unreadable search (CountryRules.ParseSearch); anything else is a generic failure. */
export const searchErrorCodes = ['full_number_required', 'too_short', 'too_long'] as const;
export type SearchErrorCode = (typeof searchErrorCodes)[number];

export const asSearchError = (code: string | undefined): SearchErrorCode | null =>
  (searchErrorCodes as readonly string[]).includes(code ?? '') ? (code as SearchErrorCode) : null;

/** Blank search = the directory list. The server applies the same trim and a 100-character limit. */
export const toQuery = (text: string): string | null => (text.trim() === '' ? null : text.trim());

/** Type-ahead in a picker (MDV-AUDIT-1100): the search runs this long after the last key, and only from this many characters; each search is audited and rate-limited. */
export const typeaheadDelayMs = 350;
export const typeaheadMinChars = 2;

export const pageCount = (r: Pick<SearchResult, 'total' | 'pageSize'>) => Math.max(1, Math.ceil(r.total / r.pageSize));

/** Address lines in display order, empty parts dropped. */
export const addressLines = (a: PatientAddress): string[] =>
  [a.line1, a.line2, a.city, a.district, a.region, a.postalCode, a.makani, a.poBox].filter((p): p is string => !!p);

