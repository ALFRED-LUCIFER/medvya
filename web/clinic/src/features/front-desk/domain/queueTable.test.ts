import { describe, expect, it } from 'vitest';
import { sortQueue, type QueueRow } from './queueTable';

const row = (id: string, patientName: string, start: string, since: string): QueueRow => ({ id, patientName, doctor: 'Dr A', state: 'CheckedIn', start, since, token: null });
const rows = [row('b', 'Zara', '2026-11-12T04:00:00Z', '2026-11-12T03:50:00Z'), row('a', 'Anil', '2026-11-12T03:30:00Z', '2026-11-12T03:55:00Z')];

describe('sortQueue', () => {
  it('keeps the server order when unsorted', () => expect(sortQueue(rows, null).map(r => r.id)).toEqual(['b', 'a']));
  it('sorts by name', () => expect(sortQueue(rows, { key: 'patient', direction: 'ascending' }).map(r => r.id)).toEqual(['a', 'b']));
  it('sorts by appointment time', () => expect(sortQueue(rows, { key: 'time', direction: 'ascending' }).map(r => r.id)).toEqual(['a', 'b']));
  it('puts the longest wait first on ascending', () => expect(sortQueue(rows, { key: 'waiting', direction: 'ascending' }).map(r => r.id)).toEqual(['b', 'a']));
  it('reverses on descending', () => expect(sortQueue(rows, { key: 'waiting', direction: 'descending' }).map(r => r.id)).toEqual(['a', 'b']));
});
