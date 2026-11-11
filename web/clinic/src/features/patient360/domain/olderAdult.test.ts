import { describe, expect, it } from 'vitest';
import { emptyGeriatricForm, geriatricFormSchema, geriLines, toRecord, type GeriatricAssessment } from './olderAdult';

const none: GeriatricAssessment = {
  id: 'a', assessedAt: '2026-11-05T05:00:00Z', author: null, fallsLast12Months: null, timedUpAndGoSeconds: null, tugAtRisk: null,
  lyingSystolic: null, lyingDiastolic: null, standingSystolic: null, standingDiastolic: null, systolicDrop: null, diastolicDrop: null, orthostatic: null,
  clinicalFrailtyScale: null, miniCog: null, adl: null, iadl: null, phq2: null, nutritionNote: null,
};

describe('older adult review', () => {
  it('flags only what the server flagged, and leaves out what was not measured', () => {
    const lines = geriLines({ ...none, timedUpAndGoSeconds: 14.2, tugAtRisk: true, lyingSystolic: 146, lyingDiastolic: 80, standingSystolic: 122, standingDiastolic: 72,
      systolicDrop: 24, diastolicDrop: 8, orthostatic: true, clinicalFrailtyScale: 4 });
    expect(lines.map(l => [l.key, l.level])).toEqual([['tugAtRisk', 'alert'], ['orthostatic', 'alert'], ['cfs', 'plain']]);
    expect(lines[1]!.values).toEqual({ lying: '146/80', standing: '122/72', systolic: 24, diastolic: 8 });
    expect(geriLines(none)).toEqual([]);
  });

  it('refuses a score outside its scale, half a blood pressure and an empty form', () => {
    const issues = (f: Partial<typeof emptyGeriatricForm>) => {
      const r = geriatricFormSchema.safeParse({ ...emptyGeriatricForm, ...f });
      return r.success ? [] : r.error.issues.map(i => `${String(i.path[0])}:${i.message}`);
    };
    expect(issues({ clinicalFrailtyScale: '10', miniCog: '5' })).toEqual(['clinicalFrailtyScale:out_of_scale']);
    expect(issues({ lyingSystolic: '140' })).toEqual(['lyingSystolic:incomplete']);
    expect(issues({})).toEqual(['fallsLast12Months:empty']);
    expect(issues({ timedUpAndGoSeconds: '14,2' })).toEqual([]);
  });

  it('sends blank as null and a decimal comma as a point', () => {
    expect(toRecord({ ...emptyGeriatricForm, timedUpAndGoSeconds: '14,2', phq2: '1', nutritionNote: '  ' })).toMatchObject({ timedUpAndGoSeconds: 14.2, phq2: 1, adl: null, nutritionNote: null });
  });
});
