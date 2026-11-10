import { csrf, send } from '../../schedule/services/scheduleApi';
import type { DocumentKind, ItContactForm, OrganisationDocument, OrganisationRequest } from '../domain/organisationRequest';

// The owner's onboarding request (ADR-016, MDV-P1-062). Documents travel as the file itself; the server checks its real type and size.
export const fetchRequest = () => send<OrganisationRequest>('/v1/organisation/request');
export const uploadDocument = (kind: DocumentKind, file: File) =>
  send<OrganisationDocument>(`/v1/organisation/request/documents/${kind}`, { method: 'PUT', headers: { ...csrf, 'Content-Type': file.type }, body: file });
export const saveItContact = (form: ItContactForm) =>
  send<void>('/v1/organisation/request/it-contact', { method: 'PUT', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify(form) });
export const startNewRequest = () => send<void>('/v1/organisation/request/start-new', { method: 'POST', headers: csrf });
export const submitRequest = () => send<void>('/v1/organisation/request/submit', { method: 'POST', headers: csrf });
