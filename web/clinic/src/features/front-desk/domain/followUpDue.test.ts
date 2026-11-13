import { describe, expect, it } from 'vitest';
import { bookFollowUpPath } from './followUpDue';

describe('follow-up due (MDV-P1-102)', () => {
  const f = { id: 'w-1', dueOn: '2026-11-02', calendarId: 'd-1', calendarName: 'Dr. Arjun Rao (synthetic)' };
  it('books on the due day with the patient carried and the doctor chosen, ids and a date only', () => {
    expect(bookFollowUpPath('/schedule', 'p-1', f)).toBe('/schedule?date=2026-11-02&view=day&patient=p-1&doctor=d-1');
  });
  it('shows every column when the doctor no longer works at the facility', () => {
    expect(bookFollowUpPath('/schedule', 'p-1', { ...f, calendarName: null })).toBe('/schedule?date=2026-11-02&view=day&patient=p-1');
  });
});
