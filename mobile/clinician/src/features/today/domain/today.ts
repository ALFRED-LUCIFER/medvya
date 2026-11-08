export type Sex = 'F' | 'M';
export type QueueStatus = 'withDoctor' | 'next' | 'waiting' | 'booked';
/** A clinical flag on the up-next card. `kind` picks the icon, so it is never colour alone. */
export type Flag = { kind: 'allergy' | 'result' | 'brief'; label: string };

export type QueueEntry = { token: string; patientId: string; name: string; age: number; sex: Sex; note: string; status: QueueStatus; at: string; viaApp?: boolean };
export type Today = {
  next: { patientId: string; name: string; initials: string; age: number; sex: Sex; reason: string; room: string; flags: Flag[] } | null;
  seen: number; waiting: number; toReview: number;
  queue: QueueEntry[];
};

/** The queue in the order the desk works it: with the doctor, next, waiting, then booked by time. */
const rank: Record<QueueStatus, number> = { withDoctor: 0, next: 1, waiting: 2, booked: 3 };
export const orderQueue = (q: QueueEntry[]) => [...q].sort((a, b) => rank[a.status] - rank[b.status] || a.at.localeCompare(b.at));
