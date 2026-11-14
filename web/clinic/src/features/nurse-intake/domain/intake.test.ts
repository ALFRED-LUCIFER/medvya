import { describe, expect, it } from 'vitest';
import { allergySchema, chartAllergies, confirmAll, correctable, correctionSchema, formOf, medicineSchema, intakeFormSchema, intakeProblem, invalidKind, toRecord, undecided, weightChange, type VitalView } from './intake';

const blank = { chiefComplaint: '', SystolicBp: '', DiastolicBp: '', Pulse: '', Temperature: '', OxygenSaturation: '', Weight: '', Height: '', RespiratoryRate: '' };
const view = (o: Partial<VitalView>): VitalView => ({ kind: 'Weight', value: 72.4, flag: null, previousValue: null, previousAt: null, flagKept: false, recordedAt: '2026-10-19T04:30:00Z', ...o });

describe('intake form', () => {
  it('leaves blanks out of the save: not taken is unknown, never zero', () => {
    expect(toRecord({ ...blank, chiefComplaint: ' chest tight ', SystolicBp: '128', Temperature: '36,8' }))
      .toEqual({ chiefComplaint: 'chest tight', vitals: [{ kind: 'SystolicBp', value: 128 }, { kind: 'Temperature', value: 36.8 }] });
    expect(toRecord(blank).vitals).toEqual([]);
  });

  it.each(['abc', '12.34', '-5', '1e3', '1000'])('refuses %s as a reading', v => expect(intakeFormSchema.safeParse({ ...blank, Pulse: v }).success).toBe(false));
  it.each(['', '72', '36.8', ' 98 '])('accepts %j as a reading', v => expect(intakeFormSchema.safeParse({ ...blank, Pulse: v }).success).toBe(true));

  it('fills the form from a saved intake and leaves untaken readings blank', () => {
    expect(formOf(null)).toEqual(blank);
    const saved = formOf({ chiefComplaint: 'cough', vitals: [view({ kind: 'Pulse', value: 72 })] } as never);
    expect([saved.chiefComplaint, saved.Pulse, saved.Weight]).toEqual(['cough', '72', '']);
  });
});

describe('flags', () => {
  it('needs a decision for a flagged reading until it is kept', () => {
    expect(undecided(view({}))).toBe(false);
    expect(undecided(view({ flag: 'High' }))).toBe(true);
    expect(undecided(view({ flag: 'High', flagKept: true }))).toBe(false);
  });

  it('says the weight change with its sign', () => {
    expect(weightChange(view({ flag: 'Changed', previousValue: 70.2 }))).toBe('+2.2');
    expect(weightChange(view({ flag: 'Changed', value: 68, previousValue: 70.2 }))).toBe('-2.2');
    expect(weightChange(view({ flag: 'High', previousValue: 70 }))).toBeNull();
  });
});

describe('server answers', () => {
  it('reads a refused field, a conflict, a refusal, or anything else', () => {
    expect(intakeProblem(400, 'vitals[2].value:implausible')).toEqual({ kind: 'invalid', field: 'vitals[2].value', code: 'implausible' });
    expect(intakeProblem(409, 'handoff_blocked')).toEqual({ kind: 'conflict', code: 'handoff_blocked' });
    expect(intakeProblem(403, undefined)).toEqual({ kind: 'refused' });
    expect(intakeProblem(500, undefined)).toEqual({ kind: 'failed' });
  });

  it('names the reading a refused field belongs to by its place in what was sent', () => {
    const sent = [{ kind: 'Pulse' as const }, { kind: 'OxygenSaturation' as const }];
    expect(invalidKind('vitals[1].value', sent)).toBe('OxygenSaturation');
    expect(invalidKind('chiefComplaint', sent)).toBeNull();
  });
});

describe('allergy and medicine forms', () => {
  it('needs a substance and a status a nurse may add; "Not an allergy" is not one', () => {
    expect(allergySchema.safeParse({ substance: ' Peanuts ', reaction: '', status: 'PatientReported' }).success).toBe(true);
    expect(allergySchema.safeParse({ substance: ' ', reaction: '', status: 'Confirmed' }).success).toBe(false);
    expect(allergySchema.safeParse({ substance: 'X', reaction: '', status: 'NotAnAllergy' }).success).toBe(false);
  });

  it('keeps a medicine as the patient named it, with or without a dose', () => {
    expect(medicineSchema.safeParse({ name: 'a white tablet for sugar', dose: '', frequency: '', needsClarification: true }).success).toBe(true);
    expect(medicineSchema.safeParse({ name: '', dose: '500 mg', frequency: '', needsClarification: false }).success).toBe(false);
  });
});

describe('chartAllergies (MDV-P1-092): the strip on intake', () => {
  const a = (status: 'Confirmed' | null) => ({ id: '1', substance: 'Penicillin', reaction: null, status, source: 'PatientReported' as const, statusSetAt: null });
  it('nothing recorded and nothing confirmed is not recorded, never none', () =>
    expect(chartAllergies({ allergies: [], noKnownAllergiesConfirmed: false }).allergyState).toBe('NotRecorded'));
  it('no known allergies only once the nurse confirmed it', () =>
    expect(chartAllergies({ allergies: [], noKnownAllergiesConfirmed: true }).allergyState).toBe('NoKnownAllergies'));
  it('a listed allergy with no status yet reads as unknown', () =>
    expect(chartAllergies({ allergies: [a(null), a('Confirmed')], noKnownAllergiesConfirmed: false })).toMatchObject({
      allergyState: 'Listed', allergies: [{ status: 'Unknown' }, { status: 'Confirmed' }] }));
});

describe('one confirmation and corrections (MDV-P2-048)', () => {
  it('lists flagged readings together from two undecided ones; a kept or unflagged one does not count', () => {
    const high = view({ kind: 'SystolicBp', value: 168, flag: 'High' });
    expect(confirmAll([high])).toBe(false);
    expect(confirmAll([high, view({ kind: 'Pulse', value: 104, flag: 'High' })])).toBe(true);
    expect(confirmAll([high, view({ kind: 'Pulse', value: 104, flag: 'High', flagKept: true }), view({ kind: 'Temperature', value: 36.8 })])).toBe(false);
  });
  it('offers a correction only after the handoff while the visit waits for the doctor', () => {
    expect([correctable({ state: 'ReadyForDoctor', handedOff: true }), correctable({ state: 'WithNurse', handedOff: false }),
      correctable({ state: 'InConsult', handedOff: true }), correctable({ state: 'ReadyForDoctor', handedOff: false })]).toEqual([true, false, false, false]);
  });
  it('needs a value and a reason', () => {
    expect(correctionSchema.safeParse({ value: '72.5', reason: 'typed 7.25' }).success).toBe(true);
    expect(correctionSchema.safeParse({ value: '', reason: 'typed 7.25' }).success).toBe(false);
    expect(correctionSchema.safeParse({ value: '72.5', reason: '  ' }).success).toBe(false);
  });
});
