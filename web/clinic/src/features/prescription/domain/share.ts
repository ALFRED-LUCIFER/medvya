import { z } from 'zod';

// Mirrors the MDV-P2-020 records in src/Modules/Prescribing/Medvya.Prescribing.Application/IPrescriptions.cs (the server checks everything again).
export type DispatchState = 'Sent' | 'Dispensed' | 'Cancelled';
export type RxShare = {
  id: string; recipient: 'Patient' | 'Pharmacy'; channel: 'Sms' | 'Email'; pharmacyName: string | null; versionNo: number; replaced: boolean;
  sharedBy: string; sharedAt: string; expiresAt: string;
  dispatch: { state: DispatchState; pharmacyRef: string | null; dispensedAt: string | null; changedBy: string; changedAt: string } | null;
};

/** Who the link goes to: the patient by SMS or email, or a named pharmacy by email. */
export const shareTargets = ['PatientSms', 'PatientEmail', 'Pharmacy'] as const;
export type ShareTarget = (typeof shareTargets)[number];

export const shareSchema = z.discriminatedUnion('target', [
  z.object({ target: z.enum(['PatientSms', 'PatientEmail']), pharmacyName: z.string(), pharmacyEmail: z.string() }),
  z.object({ target: z.literal('Pharmacy'), pharmacyName: z.string().trim().min(1).max(100), pharmacyEmail: z.email().max(254) }),
]);
export type ShareForm = z.infer<typeof shareSchema>;
export const emptyShare = (): ShareForm => ({ target: 'PatientSms', pharmacyName: '', pharmacyEmail: '' });

export const toShareBody = (f: ShareForm) =>
  f.target === 'Pharmacy'
    ? { recipient: 'Pharmacy', pharmacyName: f.pharmacyName.trim(), pharmacyEmail: f.pharmacyEmail.trim() }
    : { recipient: 'Patient', channel: f.target === 'PatientSms' ? 'Sms' : 'Email' };

/** Column definitions are data; the label for each is the i18n key `rxShare.col.<key>`. */
export const shareColumns = ['recipient', 'sharedAt', 'state', 'actions'] as const;

/**
 * Recording a dispensed dispatch: the pharmacy's reference (optional) and when it was dispensed.
 * shortcut: the picker's date-time is read in the browser's zone (a clinic's own computer, in the clinic's zone); convert from the facility zone if staff enter it from elsewhere.
 */
export const dispensedSchema = z.object({ pharmacyRef: z.string().trim().max(50), dispensedAt: z.string().min(1) });
export type DispensedForm = z.infer<typeof dispensedSchema>;
export const toDispensedBody = (f: DispensedForm) => ({ state: 'Dispensed' as const, pharmacyRef: f.pharmacyRef || null, dispensedAt: new Date(f.dispensedAt).toISOString() });

/** A share refusal the screen words: the codes ShareAsync / DispatchAsync throw; anything else is "failed". */
export type ShareProblem = 'not_authorised' | 'changed' | 'no_mobile' | 'no_address' | 'send_failed' | 'dispatch_final' | 'failed';
const shareProblems: readonly string[] = ['not_authorised', 'changed', 'no_mobile', 'no_address', 'send_failed', 'dispatch_final'];
export const shareProblem = (code: string | undefined): ShareProblem => (code && shareProblems.includes(code) ? (code as ShareProblem) : 'failed');

/** What the public link page says after a refusal: 404 is a link that is unknown or expired (the server tells them apart for nobody). */
export type OpenProblem = 'unknown' | 'replaced' | 'wrong_code' | 'no_code' | 'too_soon' | 'code_limit' | 'failed';
export function openProblem(status: number | undefined, code: string | undefined): OpenProblem {
  if (status === 404) return 'unknown';
  return code && ['replaced', 'wrong_code', 'no_code', 'too_soon', 'code_limit'].includes(code) ? (code as OpenProblem) : 'failed';
}

/** The one-time code as typed: six digits. */
export const codeSchema = z.object({ code: z.string().trim().regex(/^\d{6}$/) });
export type CodeForm = z.infer<typeof codeSchema>;
