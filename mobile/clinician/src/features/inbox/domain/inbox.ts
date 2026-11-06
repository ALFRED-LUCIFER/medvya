export type ResultItem = { id: string; patient: string; test: string; value: string; severity: 'critical' | 'abnormal'; acknowledged: boolean };
export type MessageItem = { id: string; patient: string; initials: string; text: string; replyBy: string };
export type Inbox = { results: ResultItem[]; messages: MessageItem[] };

/** The tab badge counts what still needs a person: unacknowledged results and unanswered messages. */
export const inboxCount = (i: Inbox) => i.results.filter(r => !r.acknowledged).length + i.messages.length;
