import { consentPolicyVersion, type ConsentDecision } from '../../registration/domain/registration';

/** The consent rows the desk can change from the record (MDV-P1-019); treatment is shown but cannot be withdrawn here. */
export const consentRows = [
  { id: 'WhatsApp', type: 'Reminders', channel: 'WhatsApp', title: 'registration.consents.channel.whatsapp' },
  { id: 'Sms', type: 'Reminders', channel: 'Sms', title: 'registration.consents.channel.sms' },
  { id: 'Email', type: 'Reminders', channel: 'Email', title: 'registration.consents.channel.email' },
  { id: 'Exchange', type: 'InformationExchange', channel: null, title: 'patients.consents.exchange' },
  { id: 'Portal', type: 'Portal', channel: null, title: 'registration.consents.portal.title' },
  { id: 'Marketing', type: 'Marketing', channel: null, title: 'registration.consents.marketing.title' },
] as const satisfies readonly (Pick<ConsentDecision, 'type' | 'channel'> & { id: string; title: string })[];

export type ConsentRow = (typeof consentRows)[number];
export type ConsentState = ConsentDecision & { decidedAt: string; policyVersion: string };

/** What the record's consent section needs from its controller. */
export type PatientConsents = {
  states: ConsentState[]; loading: boolean; failed: boolean; changing: string | null; changeFailed: boolean;
  onChange: (row: ConsentRow, granted: boolean) => void;
};

/** Newest decision for a row; no receipt means no consent ("not recorded"), never a guessed yes. */
export const stateOf = (states: readonly ConsentState[], row: Pick<ConsentRow, 'type' | 'channel'>) =>
  states.find(s => s.type === row.type && s.channel === row.channel) ?? null;

/** The change to send: one decision, under the wording version the screen shows. */
export const changeBody = (row: Pick<ConsentRow, 'type' | 'channel'>, granted: boolean) =>
  ({ policyVersion: consentPolicyVersion, decisions: [{ type: row.type, channel: row.channel, granted }] });
