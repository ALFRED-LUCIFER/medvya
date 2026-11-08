import { z } from 'zod';

// Clinical authorisation (MDV-P2-024). The server decides every access; these types only describe what it answers.

/** Which clinicians treat the facility's patients. */
export const clinicalPolicies = ['AllFacilityClinicians', 'AssignedOnly'] as const;
export type ClinicalPolicy = (typeof clinicalPolicies)[number];
export const clinicalPolicySchema = z.object({ policy: z.enum(clinicalPolicies) });
export type ClinicalPolicyForm = z.infer<typeof clinicalPolicySchema>;

/** Why a clinician asks: a short list only, so no clinical detail is ever typed into a request. */
export const accessReasons = ['CoveringColleague', 'SeeingPatientToday', 'ReviewingResults', 'Referral'] as const;
export type AccessReason = (typeof accessReasons)[number];

// GET /v1/access-requests: the owner's queue at the session's facility. Names are shown so the owner can decide.
export type PendingAccessRequest = {
  id: string; patientId: string; patientName: string; mrn: string; requestedBy: string; requesterName: string | null; reason: AccessReason; requestedAt: string;
};
export type AccessQueue = { requests: PendingAccessRequest[]; grantDays: number[] };

/** Why a decision did not go through: the owner's own request, already decided by someone else, or anything else. */
export type DecideError = 'own_request' | 'not_pending' | 'failed';

/** A 403 from a clinical endpoint (MDV-P2-024): asking for access can help (no care relationship) or not (the role). */
export type ClinicalRefusal = { canRequest: boolean; requestPending: boolean };

/** Reads the refusal from a 403 problem body; anything else is not one. */
export function refusalOf(body: unknown): ClinicalRefusal | null {
  if (typeof body !== 'object' || body === null) return null;
  const { code, requestPending } = body as { code?: unknown; requestPending?: unknown };
  if (code !== 'no_care_relationship' && code !== 'no_access') return null;
  return { canRequest: code === 'no_care_relationship', requestPending: requestPending === true };
}
