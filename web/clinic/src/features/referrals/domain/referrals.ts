import { z } from 'zod';
import type { ChartSource } from '../../patient360/domain/chart';
import type { SexAtBirth } from '../../patients/domain/directory';

// Mirrors src/Modules/Prescribing/Medvya.Prescribing.Application/IReferrals.cs (the server is the authority; these checks only save a round trip).
export const referralStates = ['Draft', 'Sent', 'AppointmentBooked', 'ReportReceived', 'Closed', 'Received'] as const;
export type ReferralState = (typeof referralStates)[number];
export const urgencies = ['Routine', 'Urgent'] as const;
export type Urgency = (typeof urgencies)[number];
export const channels = ['Print', 'Email', 'PatientCarries'] as const;
export type Channel = (typeof channels)[number];
export const sectionNames = ['Allergies', 'Problems', 'Medicines'] as const;
export type SectionName = (typeof sectionNames)[number];

export type ReferralStep = { state: ReferralState; at: string; documentId: string | null; note: string | null };
export type Referral = {
  id: string; patientId: string; direction: 'Out' | 'In'; provider: string; urgency: Urgency; reason: string | null; sections: SectionName[];
  state: ReferralState; channel: Channel | null; sentAt: string | null; open: boolean; letterDocumentId: string | null; history: ReferralStep[];
};
export type LetterLine = { text: string; source: ChartSource };
export type Letter = {
  patient: { fullName: string; mrn: string; dateOfBirth: string; sexAtBirth: SexAtBirth }; facilityName: string; doctorName: string | null; provider: string;
  urgency: Urgency; reason: string | null; at: string; sections: { section: SectionName; lines: LetterLine[] }[];
};

/** Column definitions are data; the label for each is the i18n key `referrals.col.<key>`. */
export const referralColumns = ['provider', 'urgency', 'state', 'channel', 'actions'] as const;
export const openReferralColumns = ['provider', 'urgency', 'state'] as const;

/** The moves the server allows from a state (it checks again): forward only, and any open referral may be closed. */
export function nextStates(state: ReferralState): ReferralState[] {
  switch (state) {
    case 'Draft': return ['Closed'];
    case 'Sent': return ['AppointmentBooked', 'ReportReceived', 'Closed'];
    case 'AppointmentBooked': return ['ReportReceived', 'Closed'];
    case 'ReportReceived': return ['Closed'];
    case 'Received': return ['Closed'];
    default: return [];
  }
}

export const canSend = (r: Referral) => r.direction === 'Out' && r.state === 'Draft';

/** Newest evidence first is not needed here: the history reads oldest first, as it happened. */
export const evidenceOf = (r: Referral) => r.history.filter(s => s.documentId !== null);

export const referralSchema = z.object({
  provider: z.string().trim().min(1).max(200),
  urgency: z.enum(urgencies),
  reason: z.string().trim().min(1).max(500),
  sections: z.array(z.enum(sectionNames)),
});
export type ReferralForm = z.infer<typeof referralSchema>;
export const emptyReferral = (): Partial<ReferralForm> => ({ provider: '', urgency: 'Routine', reason: '', sections: [] });

export const fileInSchema = z.object({ provider: z.string().trim().min(1).max(200), reason: z.string().trim().max(500), documentId: z.string().min(1) });
export type FileInForm = z.infer<typeof fileInSchema>;
export const toFileInBody = (patientId: string, f: FileInForm) => ({ patientId, provider: f.provider, reason: f.reason || null, documentId: f.documentId });

export const advanceSchema = z.object({ state: z.enum(referralStates), documentId: z.string(), note: z.string().trim().max(200) });
export type AdvanceForm = z.infer<typeof advanceSchema>;
/** A blank note or document is not sent. */
export const toAdvanceBody = (f: AdvanceForm) => ({ state: f.state, documentId: f.documentId || null, note: f.note || null });

/** What a refusal is worded as (the screen has a sentence for each). */
export type ReferralProblem = 'required' | 'too_long' | 'invalid' | 'unknown' | 'consent_required' | 'not_allowed' | 'already_sent' | 'failed';
const problems: readonly string[] = ['required', 'too_long', 'invalid', 'unknown', 'consent_required', 'not_allowed', 'already_sent'];

/** The server's answer as a problem: a 409 carries a code; a 400 carries "field:code". Anything else reads as "failed". */
export function problemOf(code: string | undefined): ReferralProblem {
  const reason = code?.includes(':') ? code.split(':')[1] : code;
  return reason && problems.includes(reason) ? (reason as ReferralProblem) : 'failed';
}
