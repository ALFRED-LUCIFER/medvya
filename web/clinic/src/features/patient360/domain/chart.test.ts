import { describe, expect, it } from 'vitest';
import { bannerAlerts, bannerAllergies, bannerAllergyState, otherVitals, parseSection, resultRows, showsSection, sparkline, vitalTiles, type Patient360, type Trend, type Vital } from './chart';

const chart = (over: Partial<Patient360['chart']>): Patient360['chart'] => ({
  allergyState: 'NotRecorded', allergies: [], problems: [], medicines: [], lastVitals: [], trends: [], recentEncounters: [], openEncounterId: null, startableAppointmentId: null, lastLabs: [], bmi: null, history: [], bloodGroup: null, careTeam: [], ...over,
});
const allergy = (status: 'Confirmed' | 'PatientReported' | 'Unknown' | 'NotAnAllergy') => ({ substance: 'Penicillin', reaction: null, status, source: 'ClinicianVerified' as const });

describe('bannerAllergyState (MDV-P1-092): the same three answers for PatientBanner', () => {
  const words = (s: string) => `[${s}]`;
  it('not recorded, and only cleared entries, read as not recorded', () => {
    expect(bannerAllergyState(chart({}), words)).toEqual({ state: 'notRecorded' });
    expect(bannerAllergyState(chart({ allergyState: 'Listed', allergies: [allergy('NotAnAllergy')] }), words)).toEqual({ state: 'notRecorded' });
  });
  it('no known allergies is said, never inferred', () => expect(bannerAllergyState(chart({ allergyState: 'NoKnownAllergies' }), words)).toEqual({ state: 'noneKnown' }));
  it('listed allergies keep their status words', () =>
    expect(bannerAllergyState(chart({ allergyState: 'Listed', allergies: [allergy('Confirmed'), allergy('PatientReported')] }), words)).toEqual({
      state: 'recorded', allergies: [{ substance: 'Penicillin', reaction: '[Confirmed]' }, { substance: 'Penicillin', reaction: '[PatientReported]' }],
    }));
});

describe('bannerAllergies: not recorded, none and listed are three different answers', () => {
  it('not recorded is null, never an empty list', () => expect(bannerAllergies(chart({}))).toBeNull());
  it('no known allergies is an empty list', () => expect(bannerAllergies(chart({ allergyState: 'NoKnownAllergies' }))).toEqual([]));
  it('lists the substance with its status', () =>
    expect(bannerAllergies(chart({ allergyState: 'Listed', allergies: [allergy('Confirmed'), allergy('PatientReported')] }))).toEqual([
      { name: 'Penicillin', status: 'verified' }, { name: 'Penicillin', status: 'patientReported' }]));
  it('leaves out cleared entries, and never reads as "none" when nothing is left', () =>
    expect(bannerAllergies(chart({ allergyState: 'Listed', allergies: [allergy('NotAnAllergy')] }))).toBeNull());
});

const trend = (code: Trend['code'], values: number[], unit = 'kg'): Trend =>
  ({ code, unit, points: values.map((value, i) => ({ value, takenAt: `2026-10-0${i + 1}T04:30:00Z`, source: 'ClinicianVerified' as const })) });

describe('sparkline', () => {
  it('runs left to right and puts a higher value higher up (smaller y)', () => {
    const [a, b, c] = sparkline([70, 75, 72]);
    expect(a!.x).toBeLessThan(b!.x);
    expect(b!.x).toBeLessThan(c!.x);
    expect(b!.y).toBeLessThan(a!.y);
  });
  it('one reading is one point in the middle, equal readings a flat line', () => {
    expect(sparkline([7])).toHaveLength(1);
    expect(new Set(sparkline([7, 7, 7]).map(p => p.y)).size).toBe(1);
  });
});

const vital = (code: Vital['code'], value: number, flag: Vital['flag'] = null, range: string | null = null): Vital =>
  ({ code, value, unit: 'mmHg', takenAt: '2026-10-03T04:30:00Z', source: 'ClinicianVerified', flag, range });

describe('vitalTiles', () => {
  it('gives the five measured tiles in order, joins systolic and diastolic, and keeps both flags', () => {
    const tiles = vitalTiles(chart({
      lastVitals: [vital('BloodPressureDiastolic', 86, 'High', '60–80'), vital('BloodPressureSystolic', 138, 'High', '90–130'), vital('Pulse', 82)],
      trends: [trend('BloodPressureSystolic', [128, 138], 'mmHg'), trend('BloodPressureDiastolic', [80, 86], 'mmHg')],
    }));
    expect(tiles.map(t => t.key)).toEqual(['BloodPressure', 'Pulse', 'OxygenSaturation', 'Temperature', 'Weight']);
    expect(tiles[0]!.reading).toMatchObject({ value: '138/86', count: 2, flags: [{ code: 'BloodPressureSystolic', flag: 'High' }, { code: 'BloodPressureDiastolic', flag: 'High' }] });
    expect(tiles[0]!.reading!.second).toHaveLength(2);
    expect(Math.max(...tiles[0]!.reading!.second.map(p => p.y))).toBeGreaterThan(Math.max(...tiles[0]!.reading!.points.map(p => p.y))); // diastolic below, one scale
    expect(tiles[1]!.reading).toMatchObject({ value: '82', count: 1, flags: [] }); // no trend yet: one point
    expect(tiles[2]!.reading).toBeNull(); // never taken: the tile says so, nothing guessed
  });
  it('a flag without a range is not shown as a flag', () =>
    expect(vitalTiles(chart({ lastVitals: [vital('Pulse', 130, 'High', null)] }))[1]!.reading!.flags).toEqual([]));
});

describe('otherVitals and resultRows', () => {
  it('lists the vitals that have no tile, so nothing is dropped', () =>
    expect(otherVitals([vital('Pulse', 82), vital('RespiratoryRate', 24), vital('BloodPressureStandingSystolic', 110)]).map(v => v.code)).toEqual(['RespiratoryRate', 'BloodPressureStandingSystolic']));
  it('gives each lab its own trend', () => {
    const rows = resultRows(chart({ lastLabs: [vital('HbA1c', 7.4), vital('Egfr', 52)], trends: [trend('HbA1c', [8.1, 7.6, 7.4], '%')] }));
    expect(rows.map(r => [r.lab.code, r.count, r.points.length])).toEqual([['HbA1c', 3, 3], ['Egfr', 1, 1]]);
  });
  it('alerts are the flagged labs with a range', () =>
    expect(bannerAlerts([vital('Egfr', 52, 'Low', '≥ 60'), vital('Sodium', 140), vital('Tsh', 9, 'High', null)]).map(v => v.code)).toEqual(['Egfr']));
});

describe('sections', () => {
  it('everything shows every part; one part shows only itself', () => {
    expect(showsSection('all', 'history')).toBe(true);
    expect(showsSection('summary', 'summary')).toBe(true);
    expect(showsSection('summary', 'history')).toBe(false);
  });
  it('a stale or edited stored value falls back to everything', () => {
    expect(parseSection('history')).toBe('history');
    expect(parseSection('nope')).toBe('all');
    expect(parseSection(null)).toBe('all');
  });
});
