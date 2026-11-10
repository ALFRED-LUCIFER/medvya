import { describe, expect, it } from 'vitest';
import { addMember, emptyForm, formFieldOf, preview, stepOfPath, stepSchema, toData, toForm, type Day } from './onboarding';

const issues = (step: number, f: ReturnType<typeof emptyForm>) => {
  const r = stepSchema(step).safeParse(f);
  return r.success ? [] : r.error.issues.map(i => `${String(i.path[0])}:${i.message}`);
};

describe('set-up wizard model (6 Nov steps)', () => {
  it('keeps what was typed through a save and a resume', () => {
    const form = { ...emptyForm('IN'), orgType: 'Hospital' as const, contactEmail: 'desk@example.test', fullName: 'Arjun Rao (synthetic)', clinicName: 'Rao Clinic (synthetic)', specialties: ['general-practice', 'diabetes'],
      authority: 'TNMC', number: '84213', expiresOn: '2031-12-31', days: ['Monday', 'Wednesday', 'Saturday', 'Sunday'] as Day[],
      newVisitFee: '1000', team: [{ email: 'joseph@example.test', role: 'Receptionist' as const }], patients: 'Move' as const };
    const data = toData(form);
    expect(data.hours).toMatchObject({ newVisitFee: 100000, slotMinutes: 15, days: ['Monday', 'Wednesday', 'Saturday', 'Sunday'], saturday: [{ start: '09:00', end: '14:00' }] });
    expect(data.profile?.specialties).toEqual(['general-practice', 'diabetes']);
    expect(data.team).toEqual([{ email: 'joseph@example.test', role: 'Receptionist' }]);
    expect(data.patients).toBe('Move');
    expect(data.clinic).toMatchObject({ type: 'Hospital', contactEmail: 'desk@example.test' });
    expect(toForm(JSON.parse(JSON.stringify(data)), 'IN')).toEqual(form);
  });

  it('sends no licence for an owner who is not a doctor; a closed Saturday sends no Saturday hours; blank stays blank', () => {
    const data = toData({ ...emptyForm('AE'), mode: 'Clinic', authority: 'DHA', number: 'GD-1', days: ['Monday', 'Tuesday'] });
    expect(data.licence).toBeUndefined();
    expect(data.hours?.saturday).toEqual([]);
    expect(data.hours?.weekdays).toEqual([{ start: '08:00', end: '14:00' }, { start: '16:00', end: '21:00' }]);
    expect(data.team).toEqual([]);
    expect(data.patients).toBeUndefined();
  });

  it('reads a draft saved before the day switches as the server does: Monday to Friday, plus Saturday when it had hours', () => {
    expect(toForm({ hours: { weekdays: [{ start: '09:00', end: '13:00' }], saturday: [] } }, 'IN').days).toEqual(['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']);
    expect(toForm({ hours: { weekdays: [], saturday: [{ start: '10:00', end: '12:00' }] } }, 'IN')).toMatchObject({ days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'], saturdayStart: '10:00' });
    expect(toForm({ profile: { specialty: 'ENT' } }, 'IN').specialty).toBe('ENT'); // kept for the server, which accepts it as the specialty
  });

  it('answers "required" at once, per step and mode', () => {
    expect(issues(1, emptyForm('IN'))).toEqual(['orgType:required', 'contactEmail:required', 'fullName:required', 'clinicName:required', 'address:required', 'specialties:required', 'authority:required', 'number:required', 'expiresOn:required', 'attested:required']);
    expect(issues(1, { ...emptyForm('IN'), mode: 'Clinic' })).toEqual(['orgType:required', 'contactEmail:required', 'fullName:required', 'clinicName:required', 'address:required']); // no licence, no specialty
    expect(issues(1, { ...emptyForm('IN'), mode: 'Clinic', orgType: 'Hospital', contactEmail: 'desk@example.test', fullName: 'K', clinicName: 'C', address: 'A' })).toEqual([]);
    expect(issues(2, emptyForm('IN'))).toEqual([]); // the design defaults are complete
    expect(issues(2, { ...emptyForm('IN'), days: [], newVisitFee: ' ' })).toEqual(['days:required', 'newVisitFee:required']);
    expect(issues(2, { ...emptyForm('IN'), weekdayStart1: '', weekdayEnd1: '' })).toEqual(['weekdayStart1:required']);
    expect(issues(2, { ...emptyForm('IN'), weekdayStart1: '', weekdayEnd1: '', days: ['Saturday'] })).toEqual([]); // Saturday has its own hours
    expect(issues(3, { ...emptyForm('IN'), inviteEmail: 'typed@example.test' })).toEqual(['inviteEmail:pending']); // typed but not invited
    expect([4, 5].flatMap(s => issues(s, emptyForm('IN')))).toEqual([]);
  });

  it('adds a colleague once, with a checked address, up to five', () => {
    const one = addMember([], ' joseph@example.test ', 'Receptionist');
    expect(one).toEqual({ team: [{ email: 'joseph@example.test', role: 'Receptionist' }] });
    const team = 'team' in one ? one.team : [];
    expect(addMember(team, 'JOSEPH@example.test', 'Nurse')).toEqual({ error: 'duplicate' });
    expect(addMember(team, '', 'Nurse')).toEqual({ error: 'required' });
    expect(addMember(team, 'not an email', 'Nurse')).toEqual({ error: 'format' });
    const full = Array.from({ length: 5 }, (_, i) => ({ email: `s${i}@example.test`, role: 'Nurse' as const }));
    expect(addMember(full, 'six@example.test', 'Nurse')).toEqual({ error: 'range' });
  });

  it('puts a server error under the right field and finds its step', () => {
    expect(formFieldOf('licence.number')).toBe('number');
    expect(formFieldOf('licence.attested')).toBe('attested');
    expect(formFieldOf('profile.specialties')).toBe('specialties');
    expect(formFieldOf('hours.days')).toBe('days');
    expect(formFieldOf('team.1.role')).toBe('team.1.email');
    expect(formFieldOf('clinic.type')).toBe('orgType');
    expect(formFieldOf('clinic.contactEmail')).toBe('contactEmail');
    expect(formFieldOf('step')).toBeNull();
    expect(['profile.fullName', 'licence.number', 'clinic.taxId', 'clinic.type', 'clinic.contactEmail', 'hours.slotMinutes', 'templates', 'team.1.email', 'patients'].map(stepOfPath)).toEqual([1, 1, 1, 1, 1, 2, 2, 3, 4]);
  });

  it('previews the booking page from what is typed', () => {
    const p = preview({ ...emptyForm('IN'), clinicName: 'Iyer family clinic', address: '12 Test Street, Chennai', specialties: ['ent', 'dental', 'diabetes', 'cardiology'] });
    expect(p).toMatchObject({ initials: 'IF', city: 'Chennai', specialties: ['ent', 'dental', 'diabetes'], firstDay: 'Monday', lastDay: 'Saturday',
      hours: '09:00–13:00 · 17:00–20:00', fee: 80000, slotMinutes: 15, slots: ['09:00', '09:15', '09:30', '10:00', '10:15', '11:00'] });
    expect(preview({ ...emptyForm('IN'), days: ['Saturday'] }).hours).toBe('09:00–14:00'); // only Saturday open: its own hours
    expect(preview({ ...emptyForm('IN'), days: [], slotMinutes: '', newVisitFee: 'abc' })).toMatchObject({ firstDay: null, slots: [], initials: '', fee: null });
  });
});
