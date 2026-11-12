import { apiFetch } from '../../api/services/apiClient';
import { ApiError, csrf, post, send } from '../../schedule/services/scheduleApi';
import type { PaperVerify, RxAdvice, RxAuthorisationState, RxLayout, RxPrint, RxVerify } from '../domain/authorisation';
import type { RxChecks } from '../domain/checks';
import { toShareBody, type RxShare, type ShareForm } from '../domain/share';
import { toSigBody, type Medicine, type RxLine, type SigDraft } from '../domain/prescription';

// The draft prescription (MDV-P2-015). Ids in paths; medicine names and dose fields in bodies. The formulary search carries a medicine name
// the doctor typed, never a patient value.
const put = <T>(url: string, body: unknown) => send<T>(url, { method: 'PUT', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
export const fetchPrescription = (encounterId: string) => send<RxLine[]>(`/v1/encounters/${encounterId}/prescription`);
export const addFromFormulary = (encounterId: string, formularyItemId: string) => post<RxLine[]>(`/v1/encounters/${encounterId}/prescription/items`, { formularyItemId });
export const addFreeText = (encounterId: string, medicine: string) => post<RxLine[]>(`/v1/encounters/${encounterId}/prescription/items`, { medicine: medicine.trim() });
export const saveSig = (itemId: string, draft: SigDraft) => put<RxLine[]>(`/v1/prescription-items/${itemId}`, toSigBody(draft));
export const includeLine = (itemId: string, included: boolean) => put<RxLine[]>(`/v1/prescription-items/${itemId}/included`, { included });
export const searchMedicines = (query: string) => send<Medicine[]>(`/v1/formulary?${new URLSearchParams({ q: query, limit: '8' })}`);
// Safety checks (MDV-P2-016): the alert key and the doctor's reason travel in the body.
export const fetchChecks = (encounterId: string) => send<RxChecks>(`/v1/encounters/${encounterId}/prescription/checks`);
export const overrideAlert = (encounterId: string, key: string, reason: string) =>
  post<RxChecks>(`/v1/encounters/${encounterId}/prescription/overrides`, { key, reason: reason.trim() });

// Authorisation and print (MDV-P2-017): authorising is its own call (the server asks for the step-up and runs the checks again).
export const fetchAuthorisation = (encounterId: string) => send<RxAuthorisationState>(`/v1/encounters/${encounterId}/prescription/authorisation`);
export const authorise = (encounterId: string) => post<RxAuthorisationState>(`/v1/encounters/${encounterId}/prescription/authorisation`, {});
/** Cancel without a replacement (step-up as for authorising); the doctor's reason travels in the body. */
export const cancelPrescription = (encounterId: string, reason: string) =>
  post<RxAuthorisationState>(`/v1/encounters/${encounterId}/prescription/cancellation`, { reason: reason.trim() });
// The advice for the patient (MDV-P2-043): its text travels in the body.
export const fetchAdvice = (encounterId: string) => send<RxAdvice>(`/v1/encounters/${encounterId}/prescription/advice`);
export const saveAdvice = (encounterId: string, text: string) => put<RxAdvice>(`/v1/encounters/${encounterId}/prescription/advice`, { text: text.trim() || null });
export const fetchRxPrint = (encounterId: string) => send<RxPrint>(`/v1/encounters/${encounterId}/prescription/print`);
export const fetchLayout = () => send<RxLayout>('/v1/prescription-layout');
export const approveLayout = () => post<RxLayout>('/v1/prescription-layout/approval', {});
/** Public: the token from the QR code travels in the body, never in a query string. */
export const verifyRx = (token: string) => post<RxVerify>('/v1/prescriptions/verify', { token });
export const verifyPaper = (token: string) => post<PaperVerify>('/v1/papers/verify', { token });

/** A server paper (investigation slip or referral letter): each POST renders, stores and audits a new copy; saved under a name with no patient data. */
export async function downloadPaperPdf(path: string, name: string): Promise<void> {
  const res = await apiFetch(path, { method: 'POST', credentials: 'same-origin', headers: csrf });
  if (!res.ok) throw new ApiError(res.status);
  await save(res, name);
}

/** The server renders and keeps the PDF (POST: each copy handed out is stored and audited); saved under a name with no patient data. */
export async function downloadRxPdf(encounterId: string, paper: 'A4' | 'A5'): Promise<void> {
  const res = await apiFetch(`/v1/encounters/${encounterId}/prescription/pdf?paper=${paper}`, { method: 'POST', credentials: 'same-origin', headers: csrf });
  if (!res.ok) throw new ApiError(res.status);
  await save(res, `prescription-${paper.toLowerCase()}.pdf`);
}

async function save(res: Response, name: string) {
  const url = URL.createObjectURL(await res.blob());
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  link.click();
  URL.revokeObjectURL(url);
}

// Sharing (MDV-P2-020): the patient's address is read on the server; a pharmacy's travels in the body. Never in a URL.
export const fetchShares = (encounterId: string) => send<RxShare[]>(`/v1/encounters/${encounterId}/prescription/shares`);
export const shareRx = (encounterId: string, form: ShareForm) => post<RxShare[]>(`/v1/encounters/${encounterId}/prescription/shares`, toShareBody(form));
export const recordDispatch = (shareId: string, body: { state: 'Dispensed' | 'Cancelled'; pharmacyRef?: string | null; dispensedAt?: string }) =>
  post<RxShare[]>(`/v1/prescription-shares/${shareId}/dispatch`, body);
/** Public: the link token travels in the body. */
export const sendSharedCode = (token: string) => post<void>('/v1/prescriptions/shared/code', { token });

/** Public: the shared PDF once the code matches, saved under a name with no patient data. */
export async function openSharedRx(token: string, code: string): Promise<void> {
  const res = await apiFetch('/v1/prescriptions/shared/open', {
    method: 'POST', credentials: 'same-origin', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify({ token, code }),
  });
  if (!res.ok) throw new ApiError(res.status, ((await res.json().catch(() => null)) as { code?: string } | null)?.code);
  await save(res, 'prescription.pdf');
}
