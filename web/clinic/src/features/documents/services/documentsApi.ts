import { csrf, post, send } from '../../schedule/services/scheduleApi';
import type { DocumentList, DocumentRow, DocumentSource, DocumentType, ViewLink } from '../domain/documents';

// Documents (MDV-P2-006). Ids only in paths; the file travels as the request body, and the type, date and source in the query are codes and a date.
export const fetchDocuments = (patientId: string) => send<DocumentList>(`/v1/patients/${patientId}/documents`);

/** A phone photo over a clinic's connection can take longer than the shared 15 seconds; a POST is never repeated automatically. */
const uploadTimeoutMs = 120_000;

export type UploadDetails = { type: DocumentType; date: string; source: DocumentSource };

/** The file as the raw body: no multipart, no name sent (a file name is often a patient's name). The server decides the type from the bytes. */
export function uploadDocument(patientId: string, details: UploadDetails, file: Blob): Promise<DocumentRow> {
  const query = new URLSearchParams({ type: details.type, date: details.date, source: details.source });
  return send<DocumentRow>(`/v1/patients/${patientId}/documents?${query}`, { method: 'POST', headers: { ...csrf, 'Content-Type': 'application/octet-stream' }, body: file, timeout: uploadTimeoutMs } as RequestInit);
}

/** POST, not GET: issuing a link is an audited action. The link expires within five minutes. */
export const openDocument = (documentId: string) => post<ViewLink>(`/v1/documents/${documentId}/view-link`, {});
