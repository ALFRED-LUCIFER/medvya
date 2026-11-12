import { describe, expect, it } from 'vitest';
import { checkKindOf, gapKey, goalFormSchema, progressFormSchema, targetFormSchema, toGoalBody, toLimit, withPlanLine, type CareGap } from './chronicCare';

const gap = (rule: CareGap['rule'], item = 'Annual diabetic foot check'): CareGap => ({ rule, action: 'AddToVisit', item, lastDone: null, since: null, vaccine: null, dose: null, referTo: null });

describe('chronic care (MDV-P2-038)', () => {
  it('adds the gap line to the plan once, after what is there', () => {
    expect(withPlanLine(undefined, 'Falls risk review')).toBe('Falls risk review');
    expect(withPlanLine('Review in 3 months\n', 'Falls risk review')).toBe('Review in 3 months\nFalls risk review');
    expect(withPlanLine('Review in 3 months\nFalls risk review', 'Falls risk review')).toBe('Review in 3 months\nFalls risk review');
  });

  it('only the eye and foot gaps can be recorded as done, and each gap has its own key', () => {
    expect(checkKindOf(gap('DiabeticFootExam'))).toBe('DiabeticFootExam');
    expect(checkKindOf(gap('UrineAlbumin'))).toBeNull();
    expect(gapKey(gap('OrderNotResulted', 'HbA1c'))).not.toBe(gapKey(gap('OrderNotResulted', 'TSH')));
  });

  it('reads a target with a decimal comma and refuses text', () => {
    expect(toLimit(targetFormSchema.parse({ limit: '7,5' }))).toBe(7.5);
    expect(targetFormSchema.safeParse({ limit: 'seven' }).success).toBe(false);
  });

  it('a goal needs its text; progress is 0 to 100, blank is 0', () => {
    expect(goalFormSchema.safeParse({ text: ' ', target: '', progress: '' }).success).toBe(false);
    expect(goalFormSchema.safeParse({ text: 'Walk', target: '', progress: '101' }).success).toBe(false);
    expect(toGoalBody(goalFormSchema.parse({ text: ' Walk 150 min a week ', target: '', progress: '' }))).toEqual({ text: 'Walk 150 min a week', target: null, progressPercent: 0 });
    expect(progressFormSchema.safeParse({ progress: '' }).success).toBe(false);
  });
});
