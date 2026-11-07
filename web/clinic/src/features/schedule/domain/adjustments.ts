import { z } from 'zod';
import type { Appointment } from './schedule';

// MDV-P1-012: leave and one-off schedule changes. Mirrors IScheduleExceptionService.cs; the server is the authority.
export const adjustmentKinds = ['Leave', 'ExtraSession', 'ShortenedDay'] as const;
export type AdjustmentKind = (typeof adjustmentKinds)[number];
export type RebookAction = 'Offer' | 'Manual';
export const rebookActions: readonly RebookAction[] = ['Offer', 'Manual'];

const date = z.string().regex(/^\d{4}-\d{2}-\d{2}$/, { message: 'date' });
const time = z.string().regex(/^\d{2}:\d{2}$/);

/** Leave takes From..To; an extra session or shortened day takes one date (From) and a window. */
export const adjustmentSchema = z
  .object({
    calendarId: z.uuid({ message: 'calendar' }),
    kind: z.enum(adjustmentKinds),
    fromDate: date,
    toDate: date,
    start: z.union([time, z.literal('')]),
    end: z.union([time, z.literal('')]),
    reason: z.string().trim().max(60, { message: 'tooLong' }),
  })
  .superRefine((v, ctx) => {
    if (v.kind === 'Leave') {
      if (v.toDate < v.fromDate) ctx.addIssue({ code: 'custom', path: ['toDate'], message: 'range' });
    } else if (!v.start || !v.end || v.start >= v.end) ctx.addIssue({ code: 'custom', path: ['start'], message: 'window' });
  });
export type AdjustmentForm = z.infer<typeof adjustmentSchema>;

export const emptyAdjustment = (today: string): Partial<AdjustmentForm> =>
  ({ kind: 'Leave', fromDate: today, toDate: today, start: '', end: '', reason: '' });

/** An exception as GET /v1/schedule/exceptions lists it. */
export type SavedAdjustment = {
  id: string; calendarId: string; kind: AdjustmentKind; fromDate: string; toDate: string; start: string | null; end: string | null;
  /** Null for staff who may not manage the calendar: they see "On leave" only (#231). */
  reason: string | null;
  /** Whether the caller may edit or delete it; sent back as `version` so a change made meanwhile is not overwritten. */
  canEdit: boolean; version: string;
};

/** Who may add leave (#231): the practice admin for every calendar, a doctor with booking access for their own only. */
export const manageableCalendars = <C extends { id: string }>(calendars: C[], canConfigure: boolean, userId: string) =>
  canConfigure ? calendars : calendars.filter(c => c.id === userId);
export type SaveResult = { exception: SavedAdjustment; offered: number; manual: number };

/** A saved exception back in the form, to edit it (times without seconds). */
export const toForm = (r: SavedAdjustment): AdjustmentForm =>
  ({ calendarId: r.calendarId, kind: r.kind, fromDate: r.fromDate, toDate: r.toDate, start: r.start?.slice(0, 5) ?? '', end: r.end?.slice(0, 5) ?? '', reason: r.reason ?? '' });

/** The request body: leave drops the window; the other kinds are one day. Times go with seconds, as the API reads them. */
export const toRequest = (facilityId: string, v: AdjustmentForm, actions: Record<string, RebookAction>) => ({
  facilityId, calendarId: v.calendarId, kind: v.kind, fromDate: v.fromDate, toDate: v.kind === 'Leave' ? v.toDate : v.fromDate,
  start: v.kind === 'Leave' ? null : `${v.start}:00`, end: v.kind === 'Leave' ? null : `${v.end}:00`, reason: v.reason || null,
  actions: Object.entries(actions).map(([appointmentId, action]) => ({ appointmentId, action })),
});

/** Every affected visit has a choice: the save stays disabled until then, as the server refuses otherwise. */
export const allChosen = (affected: Appointment[], chosen: Record<string, RebookAction>) => affected.every(a => chosen[a.id] !== undefined);

/** Codes the screen has a sentence for: field errors arrive as "field:code", 409 visit changes as "changed". */
const known = ['past', 'overlap', 'no_schedule', 'single_day', 'range', 'five_minutes', 'too_long', 'changed', 'unknown', 'has_bookings', 'stale'] as const;
export type AdjustmentError = (typeof known)[number] | 'failed' | 'forbidden';
export const adjustmentError = (code: string | undefined): AdjustmentError => {
  const last = code?.split(':').pop() ?? '';
  return (known as readonly string[]).includes(last) ? (last as AdjustmentError) : 'failed';
};
