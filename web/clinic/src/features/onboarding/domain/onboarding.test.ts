import { describe, expect, it } from 'vitest';
import { emptyForm, formFieldOf, stepOfPath, stepSchema, stepsFor, toData, toForm } from './onboarding';

describe('onboarding wizard model', () => {
  it('an owner who is not a doctor skips the licence step', () => {
    expect(stepsFor('Solo')).toEqual([1, 2, 3, 4, 5, 6]);
    expect(stepsFor('Clinic')).toEqual([1, 3, 4, 5, 6]);
  });

  it('keeps what was typed through a save and a resume', () => {
    const form = { ...emptyForm('IN'), fullName: 'Arjun Rao (synthetic)', authority: 'TNMC', number: '84213', newVisitFee: '1000', saturdayStart: '09:00', saturdayEnd: '12:00',
      templates: ['general-opd'], receptionistEmail: 'joseph@example.test' };
    const data = toData(form);
    expect(data.hours).toMatchObject({ newVisitFee: 100000, slotMinutes: 20, saturday: [{ start: '09:00', end: '12:00' }] });
    expect(data.team).toEqual([{ email: 'joseph@example.test', role: 'Receptionist' }]);
    expect(toForm(JSON.parse(JSON.stringify(data)), 'IN')).toEqual(form);
  });

  it('sends no licence for an owner who is not a doctor, and blank optional values stay blank', () => {
    const data = toData({ ...emptyForm('AE'), mode: 'Clinic', fullName: 'K. Lakshmi (synthetic)', authority: 'DHA', number: 'GD-1' });
    expect(data.licence).toBeUndefined();
    expect(data.hours?.saturday).toEqual([]);
    expect(data.team).toEqual([]);
  });

  it('answers "required" at once, per step and mode', () => {
    const issues = (step: number, f: ReturnType<typeof emptyForm>) => {
      const r = stepSchema(step).safeParse(f);
      return r.success ? [] : r.error.issues.map(i => String(i.path[0]));
    };
    expect(issues(1, emptyForm('IN'))).toEqual(['fullName', 'specialty']);
    expect(issues(1, { ...emptyForm('IN'), mode: 'Clinic' })).toEqual(['fullName']); // a non-doctor owner needs no specialty
    expect(issues(2, emptyForm('IN'))).toEqual(['authority', 'number']);
    expect(issues(3, emptyForm('IN'))).toEqual(['clinicName', 'address']);
    expect(issues(4, emptyForm('IN'))).toEqual([]); // the design defaults are complete
    expect(issues(4, { ...emptyForm('IN'), weekdayStart1: '', newVisitFee: ' ' })).toEqual(['weekdayStart1', 'newVisitFee']);
    expect(issues(5, emptyForm('IN'))).toEqual([]);
  });

  it('puts a server error under the right field, including the numbered team members', () => {
    const f = { ...emptyForm('IN'), nurseEmail: 'nurse@example.test' };
    expect(formFieldOf('licence.number', f)).toBe('number');
    expect(formFieldOf('hours.weekdays', f)).toBe('weekdayStart1');
    expect(formFieldOf('team.0.email', f)).toBe('nurseEmail'); // only the nurse was filled in, so she is member 0
    expect(formFieldOf('team.0.email', { ...f, receptionistEmail: 'r@example.test' })).toBe('receptionistEmail');
    expect(formFieldOf('step', f)).toBeNull();
  });

  it('finds the step that owns an error path', () => {
    expect(['profile.fullName', 'licence.number', 'clinic.taxId', 'hours.slotMinutes', 'templates', 'team.1.email'].map(stepOfPath)).toEqual([1, 2, 3, 4, 5, 6]);
  });
});
