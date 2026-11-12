import { describe, expect, it } from 'vitest';
import { groupOf, settingsGroups, settingsTabs, certificateProblem, closureSchema, locationProblem, hoursProblems, logoProblem, profileSchema, sessionProblem, sessionsOutsideHours, settingsError, templateBody, toDayHours, toHoursRows, type CalendarTemplate } from './settings';

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
    const ok = { name: ' Iyer Family Clinic (synthetic) ', address: '', registrationNumber: '', registrationExpiresOn: '', taxNumber: '', phone: '', email: '' };
    expect(profileSchema.safeParse(ok).success).toBe(true);
    expect(profileSchema.safeParse({ ...ok, name: '  ' }).success).toBe(false);
    expect(profileSchema.safeParse({ ...ok, phone: '+91 90000 00101', email: 'desk@clinic.example' }).success).toBe(true);
    expect(profileSchema.safeParse({ ...ok, phone: 'call me' }).success).toBe(false);
    expect(profileSchema.safeParse({ ...ok, email: 'Joseph <j@example.test>' }).success).toBe(false);
    expect(profileSchema.safeParse({ ...ok, registrationExpiresOn: '2027-03-31' }).success).toBe(true);
    expect(profileSchema.safeParse({ ...ok, registrationExpiresOn: '31/03/2027' }).success).toBe(false);
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

  it('checks a location certificate by type and size and asks for one', () => {
    expect(certificateProblem(null)).toBe('required');
    expect(certificateProblem({ type: 'application/pdf', size: 1000 })).toBeNull();
    expect(certificateProblem({ type: 'image/svg+xml', size: 1000 })).toBe('type');
    expect(certificateProblem({ type: 'image/png', size: 5 * 1024 * 1024 + 1 })).toBe('too_large');
  });

  it('puts a refusal of an added location under its field, or beside the button', () => {
    expect(locationProblem('taxNumber:format')).toEqual({ field: 'taxNumber', key: 'settings.locations.error.taxNumber.format' });
    expect(locationProblem('certificate:type')).toEqual({ field: 'certificate', key: 'settings.locations.error.certificate.type' });
    expect(locationProblem('countryCode:mismatch')).toEqual({ field: 'form', key: 'settings.locations.error.country' });
    expect(locationProblem(undefined)).toEqual({ field: 'form', key: 'settings.saveFailed' });
  });
});

describe('settings groups (MDV audit UX-04)', () => {
  it('puts every tab in exactly one group', () => {
    expect(Object.values(settingsGroups).flat().sort()).toEqual([...settingsTabs].sort());
  });
  it('gives nurse intake its own place under Clinical, and access requests under Security', () => {
    expect([groupOf('nurse'), groupOf('requests'), groupOf('staff')]).toEqual(['clinical', 'security', 'people']);
  });
});

describe('sessionsOutsideHours (MDV audit UX-13)', () => {
  const calendar = (name: string, hours: CalendarTemplate['hours']): CalendarTemplate =>
    ({ calendarId: name, calendarKind: 'Doctor', name, slotMinutes: 20, hours, breaks: [], visitLengths: [], appointmentsOutsideHours: [] });
  const s = (weekday: 'Monday' | 'Tuesday', start: string, end: string) => ({ weekday, start, end, room: null });
  const open = [{ weekday: 'Monday' as const, opens: '09:00:00', closes: '17:00:00' }];

  it('lists a session on a closed day or past the opening hours, once per doctor and day', () => {
    const rao = calendar('Dr. Rao (synthetic)', [s('Monday', '08:00:00', '10:00:00'), s('Monday', '16:00:00', '18:00:00'), s('Tuesday', '09:00:00', '12:00:00')]);
    expect(sessionsOutsideHours(open, [rao])).toEqual([{ name: 'Dr. Rao (synthetic)', weekday: 'Monday' }, { name: 'Dr. Rao (synthetic)', weekday: 'Tuesday' }]);
  });

  it('is empty when every session sits inside the hours, edges included', () => {
    expect(sessionsOutsideHours(open, [calendar('Dr. Menon (synthetic)', [s('Monday', '09:00:00', '17:00:00')])])).toEqual([]);
  });

  it('a location closed all week makes every session outside', () => {
    expect(sessionsOutsideHours([], [calendar('Dr. Menon (synthetic)', [s('Monday', '09:00:00', '12:00:00')])])).toHaveLength(1);
  });
});
