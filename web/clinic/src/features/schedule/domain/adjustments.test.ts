import { describe, expect, it } from 'vitest';
import { adjustmentError, adjustmentSchema, allChosen, toForm, toRequest, type AdjustmentForm } from './adjustments';
import type { Appointment } from './schedule';

const id = '0198a1b2-0000-7000-8000-000000000001';
const base: AdjustmentForm = { calendarId: id, kind: 'Leave', fromDate: '2026-10-19', toDate: '2026-10-21', start: '', end: '', reason: '' };
const visit = (n: string) => ({ id: n }) as Appointment;

describe('adjustment form', () => {
  it('accepts leave with a window left blank and refuses an end before the start', () => {
    expect(adjustmentSchema.safeParse(base).success).toBe(true);
    expect(adjustmentSchema.safeParse({ ...base, toDate: '2026-10-18' }).error?.issues[0].message).toBe('range');
  });

  it('needs a window for an extra session and a shortened day', () => {
    expect(adjustmentSchema.safeParse({ ...base, kind: 'ExtraSession' }).error?.issues[0].message).toBe('window');
    expect(adjustmentSchema.safeParse({ ...base, kind: 'ShortenedDay', start: '11:00', end: '10:00' }).error?.issues[0].message).toBe('window');
    expect(adjustmentSchema.safeParse({ ...base, kind: 'ShortenedDay', start: '09:00', end: '11:00' }).success).toBe(true);
  });

  it('sends one day with seconds for a window and the whole range for leave', () => {
    expect(toRequest('f', { ...base, kind: 'ExtraSession', start: '10:00', end: '11:00' }, {})).toMatchObject({ toDate: '2026-10-19', start: '10:00:00', end: '11:00:00' });
    expect(toRequest('f', base, { a: 'Offer' })).toMatchObject({ toDate: '2026-10-21', start: null, actions: [{ appointmentId: 'a', action: 'Offer' }] });
  });

  it('turns a saved exception back into the form, times without seconds', () => {
    const saved = { id: 'x', calendarId: id, kind: 'ShortenedDay' as const, fromDate: '2026-10-19', toDate: '2026-10-19', start: '09:00:00', end: '11:00:00', reason: null };
    expect(toForm(saved)).toEqual({ calendarId: id, kind: 'ShortenedDay', fromDate: '2026-10-19', toDate: '2026-10-19', start: '09:00', end: '11:00', reason: '' });
    expect(adjustmentSchema.safeParse(toForm(saved)).success).toBe(true);
  });

  it('enables saving only when every affected visit has an action', () => {
    expect(allChosen([visit('a'), visit('b')], { a: 'Offer' })).toBe(false);
    expect(allChosen([visit('a'), visit('b')], { a: 'Offer', b: 'Manual' })).toBe(true);
    expect(allChosen([], {})).toBe(true);
  });

  it('names a field error by its code', () => {
    expect(adjustmentError('fromDate:past')).toBe('past');
    expect(adjustmentError('changed')).toBe('changed');
    expect(adjustmentError('nonsense')).toBe('failed');
  });
});
