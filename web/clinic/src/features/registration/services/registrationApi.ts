import type { RegistrationStatus } from '../../patients/domain/directory';
import { fail, jsonHeaders } from '../../patients/services/patientsApi';
import type { CompletionRequest, DuplicateCandidate, DuplicateQuery, RegisterRequest, RegistrationResult } from '../domain/registration';

/** Possible existing records for what has been typed. Name, date of birth and IDs travel in the body, never in the URL. */
export async function checkDuplicates(query: DuplicateQuery): Promise<DuplicateCandidate[]> {
  const res = await fetch('/v1/patients/duplicate-check', { method: 'POST', credentials: 'same-origin', headers: jsonHeaders, body: JSON.stringify(query) });
  if (!res.ok) return fail(res);
  return ((await res.json()) as { candidates: DuplicateCandidate[] }).candidates;
}

/** Registers the patient at the active facility (the server takes facility and country from the session). */
export async function registerPatient(request: RegisterRequest): Promise<RegistrationResult> {
  const res = await fetch('/v1/patients', { method: 'POST', credentials: 'same-origin', headers: jsonHeaders, body: JSON.stringify(request) });
  if (!res.ok) return fail(res);
  return res.json() as Promise<RegistrationResult>;
}

/** Adds the address and/or payer to a quick registration of the active facility; any other facility's patient is 404. */
export async function completeRegistration(patientId: string, request: CompletionRequest): Promise<{ status: RegistrationStatus }> {
  const res = await fetch(`/v1/patients/${encodeURIComponent(patientId)}/completion`, {
    method: 'POST', credentials: 'same-origin', headers: jsonHeaders, body: JSON.stringify(request),
  });
  if (!res.ok) return fail(res);
  return res.json() as Promise<{ status: RegistrationStatus }>;
}
