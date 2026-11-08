import type { ChartSource } from '../../patient360/domain/chart';

// Documents (MDV-P2-006, E-03): what the server answers, and what may be uploaded. The server decides every access and checks every file.
export const documentTypes = ['LabReport', 'Imaging', 'Prescription', 'Referral', 'Other'] as const;
export type DocumentType = (typeof documentTypes)[number];
/** An upload is brought by the patient or comes from another provider; it is never "clinician verified" and never "synced from HIS". */
export const documentSources = ['Imported', 'PatientReported'] as const satisfies readonly ChartSource[];
export type DocumentSource = (typeof documentSources)[number];
export type ScanStatus = 'Pending' | 'Clean' | 'Infected';

export type DocumentRow = {
  id: string; type: DocumentType; documentDate: string; source: ChartSource; scanStatus: ScanStatus; contentType: string; sizeBytes: number;
  uploadedBy: string | null; uploadedAt: string;
};
export type DocumentList = { patient: { fullName: string; mrn: string; dateOfBirth: string }; documents: DocumentRow[]; canOpen: boolean; timeZone: string };
export type ViewLink = { url: string; expiresAt: string; contentType: string; viewer: string; viewedAt: string };

export const maxBytes = 10 * 1024 * 1024;
export const acceptedTypes = ['application/pdf', 'image/jpeg', 'image/png'] as const;

/** Why a file cannot be uploaded, before it is sent (the server checks again, by the file's bytes). */
export type FileProblem = 'empty' | 'too_large' | 'unsupported_type';

export function fileProblem(file: { size: number; type: string }): FileProblem | null {
  if (file.size === 0) return 'empty';
  if (file.size > maxBytes) return 'too_large';
  return (acceptedTypes as readonly string[]).includes(file.type) ? null : 'unsupported_type';
}

/** The word for a failed upload: the server's code when it has one that the screen knows, else a general failure. */
export type UploadFailure = FileProblem | 'invalid' | 'forbidden' | 'unavailable' | 'failed';

export function uploadFailure(status: number | undefined, code: string | undefined): UploadFailure {
  if (status === 413) return 'too_large';
  if (status === 415) return 'unsupported_type';
  if (status === 403) return 'forbidden';
  if (status === 503) return 'unavailable';
  if (status === 400) return code === 'empty' ? 'empty' : 'invalid';
  return 'failed';
}

/** A document is opened only once its scan passed, and only by a role that may (the server checks both). */
export const canOpenDocument = (d: DocumentRow, canOpen: boolean) => canOpen && d.scanStatus === 'Clean';

/** Keep polling while any scan is still running: the status changes by itself. */
export const anyPending = (docs: DocumentRow[]) => docs.some(d => d.scanStatus === 'Pending');

export const sizeLabel = (bytes: number) => (bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / (1024 * 1024)).toFixed(1)} MB`);

/** Today as a date input's value (YYYY-MM-DD) in a given zone. */
export const todayIn = (zone: string, now = new Date()) => new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(now);
