export type ResultItem = { id: string; patient: string; test: string; value: string; severity: 'critical' | 'abnormal'; acknowledged: boolean };
export type MessageItem = { id: string; patient: string; initials: string; text: string; replyBy: string };
export type Inbox = { results: ResultItem[]; messages: MessageItem[] };

/** The tab badge counts what still needs this person: unanswered messages, and unacknowledged results for clinicians only. */
export const inboxCount = (i: Inbox, clinical: boolean) => (clinical ? i.results.filter(r => !r.acknowledged).length : 0) + i.messages.length;
