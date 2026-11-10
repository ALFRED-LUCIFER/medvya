import { csrf, post, send } from '../../schedule/services/scheduleApi';
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
