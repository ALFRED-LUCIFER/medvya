import { post, send } from '../../schedule/services/scheduleApi';
import { toAdvanceBody, toFileInBody, type AdvanceForm, type Channel, type FileInForm, type Letter, type Referral, type ReferralForm } from '../domain/referrals';

// Referrals (MDV-P2-019). Ids in paths; provider names, reasons and notes in bodies, never in a URL.
export const fetchReferrals = (patientId: string) => send<Referral[]>(`/v1/patients/${patientId}/referrals`);
export const createReferral = (encounterId: string, form: ReferralForm) => post<Referral>(`/v1/encounters/${encounterId}/referrals`, form);
export const fileReferralIn = (patientId: string, form: FileInForm) => post<Referral>('/v1/referrals/in', toFileInBody(patientId, form));
export const sendReferral = (referralId: string, channel: Channel) => post<Referral>(`/v1/referrals/${referralId}/send`, { channel });
export const advanceReferral = (referralId: string, form: AdvanceForm) => post<Referral>(`/v1/referrals/${referralId}/status`, toAdvanceBody(form));
export const fetchLetter = (referralId: string) => send<Letter>(`/v1/referrals/${referralId}/letter`);

/** The browser's print dialog, where "Save as PDF" makes the letter's PDF. */
export const printPage = () => window.print();
