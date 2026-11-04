import { describe, expect, it } from 'vitest';
import { closureSchema, hoursProblems, logoProblem, profileSchema, sessionProblem, settingsError, templateBody, toDayHours, toHoursRows, type CalendarTemplate } from './settings';

describe('practice settings rules', () => {
  it('shows the week from Monday and sends only the open days', () => {
    const rows = toHoursRows([{ weekday: 'Saturday', opens: '10:00:00', closes: '14:00:00' }]);
    expect(rows.map(r => r.weekday)).toEqual(['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']);
    expect(toDayHours(rows)).toEqual([{ weekday: 'Saturday', opens: '10:00:00', closes: '14:00:00' }]);
  });

  it('refuses an open day that closes before it opens, and says which', () => {
    const rows = toHoursRows([]).map(r => (r.weekday === 'Friday' ? { ...r, open: true, opens: '18:00', closes: '09:00' } : r));
    expect(hoursProblems(rows)).toEqual(['Friday']);
    expect(toDayHours(rows)).toBeNull();
  });

  it('checks a logo by type and size before upload', () => {
    expect(logoProblem({ type: 'image/png', size: 1000 })).toBeNull();
    expect(logoProblem({ type: 'image/jpeg', size: 512 * 1024 })).toBeNull();
    expect(logoProblem({ type: 'image/svg+xml', size: 1000 })).toBe('type');
    expect(logoProblem({ type: 'image/gif', size: 1000 })).toBe('type');
    expect(logoProblem({ type: 'image/png', size: 512 * 1024 + 1 })).toBe('size');
    expect(logoProblem({ type: 'image/png', size: 0 })).toBe('size');
  });

  it('validates the profile and treats empty optional fields as fine', () => {
    const ok = { name: ' Iyer Family Clinic (synthetic) ', address: '', registrationNumber: '', taxNumber: '', phone: '', email: '' };
    expect(profileSchema.safeParse(ok).success).toBe(true);
    expect(profileSchema.safeParse({ ...ok, name: '  ' }).success).toBe(false);
    expect(profileSchema.safeParse({ ...ok, phone: '+91 90000 00101', email: 'desk@clinic.example' }).success).toBe(true);
    expect(profileSchema.safeParse({ ...ok, phone: 'call me' }).success).toBe(false);
    expect(profileSchema.safeParse({ ...ok, email: 'Joseph <j@example.test>' }).success).toBe(false);
  });

  it('a closure needs a real date and a name', () => {
    expect(closureSchema.safeParse({ date: '2026-11-08', name: 'Diwali' }).success).toBe(true);
    expect(closureSchema.safeParse({ date: '', name: 'Diwali' }).success).toBe(false);
    expect(closureSchema.safeParse({ date: '2026-11-08', name: ' ' }).success).toBe(false);
  });

  it('flags sessions that end before they start or overlap on a day', () => {
    const s = (weekday: 'Monday' | 'Tuesday', start: string, end: string) => ({ weekday, start, end, room: null });
    expect(sessionProblem([s('Monday', '09:00', '13:00'), s('Monday', '17:00', '20:00'), s('Tuesday', '09:00', '13:00')])).toBeNull();
    expect(sessionProblem([s('Monday', '13:00', '09:00')])).toBe('order');
    expect(sessionProblem([s('Monday', '09:00', '13:00'), s('Monday', '12:00', '15:00')])).toBe('overlap');
    expect(sessionProblem([s('Monday', '09:00', '13:00'), s('Tuesday', '12:00', '15:00')])).toBeNull();
  });

  it('saving a schedule sends breaks, rooms and visit lengths back as they were', () => {
    const t: CalendarTemplate = {
      calendarId: 'c1', calendarKind: 'Doctor', name: 'Dr. Arjun Rao (synthetic)', slotMinutes: 20, appointmentsOutsideHours: [],
      hours: [{ weekday: 'Monday', start: '09:00:00', end: '13:00:00', room: 'Room 3' }],
      breaks: [{ weekday: 'Monday', start: '12:00:00', end: '13:00:00', label: 'Lunch break' }],
      visitLengths: [{ visitType: 'NewConsultation', minutes: 40 }],
    };
    const body = templateBody('f1', t, 30, [{ weekday: 'Monday', start: '09:00', end: '12:00', room: 'Room 3' }]);
    expect(body).toMatchObject({ facilityId: 'f1', calendarId: 'c1', slotMinutes: 30, breaks: t.breaks, visitLengths: t.visitLengths });
    expect(body.hours).toEqual([{ weekday: 'Monday', start: '09:00:00', end: '12:00:00', room: 'Room 3' }]);
  });

  it('knows the codes the API reports for a rejected field', () => {
    expect([settingsError('logo:type'), settingsError('date:duplicate'), settingsError('x'), settingsError(undefined)]).toEqual(['logo:type', 'date:duplicate', 'failed', 'failed']);
  });
});
