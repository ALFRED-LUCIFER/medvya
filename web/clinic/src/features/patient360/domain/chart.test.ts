import { describe, expect, it } from 'vitest';
import { bannerAllergies, sparkline, trendLines, type Patient360, type Trend } from './chart';

const chart = (over: Partial<Patient360['chart']>): Patient360['chart'] => ({
  allergyState: 'NotRecorded', allergies: [], problems: [], medicines: [], lastVitals: [], trends: [], recentEncounters: [], openEncounterId: null, ...over,
});
const allergy = (status: 'Confirmed' | 'PatientReported' | 'Unknown' | 'NotAnAllergy') => ({ substance: 'Penicillin', reaction: null, status, source: 'ClinicianVerified' as const });

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

describe('trendLines', () => {
  it('orders HbA1c, blood pressure, weight, and joins systolic and diastolic', () => {
    const lines = trendLines([trend('Weight', [70, 71]), trend('BloodPressureDiastolic', [80, 82], 'mmHg'), trend('BloodPressureSystolic', [120, 124], 'mmHg'), trend('HbA1c', [7.9, 7.4], '%')]);
    expect(lines.map(l => [l.key, l.latest, l.count])).toEqual([['HbA1c', '7.4', 2], ['BloodPressure', '124/82', 2], ['Weight', '71', 2]]);
    expect(lines[1]!.second).toHaveLength(2);
  });
  it('leaves out a measure with no readings', () => expect(trendLines([trend('Weight', [])])).toEqual([]));
});
