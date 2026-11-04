/** A message sent to the patient about a visit, as reception sees it (MDV-P1-032/033): status and a short code, never text or address. */
export type MessageKind = 'Confirmation' | 'Rescheduled' | 'Cancelled' | 'Reminder24h' | 'Reminder2h';
export type MessageChannel = 'Email' | 'Sms';
export type MessageStatus = 'Queued' | 'Sent' | 'Delivered' | 'Failed' | 'Bounced' | 'Cancelled' | 'Suppressed';

export type PatientMessage = {
  id: string;
  kind: MessageKind;
  channel: MessageChannel;
  status: MessageStatus;
  attempts: number;
  failureCode: string | null;
  sendAt: string;
};

/** Reasons the server gives that have their own wording; any other code is shown as a generic reason. */
const knownCodes = ['no_consent', 'no_address', 'visit_started', 'visit_changed', 'dlt_template_id_missing', 'sms_sender_missing', 'sms_template_unregistered'] as const;
export type KnownCode = (typeof knownCodes)[number];

export const codeKey = (code: string | null): KnownCode | 'other' | null =>
  code === null ? null : (knownCodes as readonly string[]).includes(code) ? (code as KnownCode) : 'other';

/** Failed and bounced messages need the desk's attention (call the patient); the others are normal. */
export const needsAttention = (m: PatientMessage) => m.status === 'Failed' || m.status === 'Bounced';
