import { z } from 'zod';
import { toMinor, type Currency } from './catalogue';
import type { PaymentMethod } from './invoice';

// Mirrors src/Modules/Billing/Medvya.Billing.Application/ICashSessions.cs (MDV-P4-012). The server enforces every rule (the second person included);
// these only decide what the screen offers and show the sums as the desk types.
export type CashSessionState = 'Open' | 'AwaitingConfirmation' | 'Closed';
export type CashCount = { valueMinor: number; count: number };
/** Payments of one method in the session, and the refunds (credit notes) made that way. */
export type MethodTotal = { method: PaymentMethod; payments: number; amountMinor: number; refunds: number; refundsMinor: number };
export type CashAdjustment = { id: string; amountMinor: number; reason: string; createdByName: string; createdAt: string };
export type CashSession = {
  id: string; state: CashSessionState; currency: Currency; openingFloatMinor: number; openedByName: string; openedAt: string;
  counts: CashCount[]; totals: MethodTotal[]; expectedMinor: number; countedMinor: number | null; varianceMinor: number | null; note: string | null;
  countedByName: string | null; countedAt: string | null; confirmedByName: string | null; closedAt: string | null; mayConfirm: boolean;
  adjustments: CashAdjustment[]; version: number;
};
export type CashDay = { current: CashSession | null; awaiting: CashSession[]; denominations: number[]; currency: Currency; recent: CashSession[] };

/** The drawer form: one count per denomination (keyed by its value in minor units) and the variance note. */
export type CountForm = { counts: Record<string, string>; note: string };

const count = (v: string | undefined) => (/^\d{1,6}$/.test((v ?? '').trim()) ? Number(v) : 0);
/** What the typed counts add up to, in minor units; a blank or invalid count is 0 (the server refuses an invalid one). */
export const countedMinor = (denominations: number[], counts: Record<string, string>) => denominations.reduce((sum, d) => sum + d * count(counts[d]), 0);
export const toCounts = (denominations: number[], counts: Record<string, string>): CashCount[] => denominations.map(d => ({ valueMinor: d, count: count(counts[d]) }));
export const fromCounts = (counts: CashCount[]): Record<string, string> => Object.fromEntries(counts.map(c => [String(c.valueMinor), String(c.count)]));

/** Counted − expected: negative when the drawer is short. Any variance needs a note and a second staff member. */
export const variance = (counted: number, expected: number) => counted - expected;

export const openSchema = z.object({ float: z.string().trim().regex(/^\d{1,6}(\.\d{1,2})?$/) });
export type OpenForm = z.infer<typeof openSchema>;
export const floatMinor = (f: OpenForm) => toMinor(f.float);

/** Closed sessions table: the label of each column is `billing.dayClose.col.<key>`. */
export const sessionColumns = ['closedAt', 'openedBy', 'expected', 'counted', 'variance', 'confirmedBy', 'report'] as const;
export type SessionColumn = (typeof sessionColumns)[number];
export const sessionSort = {
  closedAt: (s: CashSession) => s.closedAt ?? '',
  expected: (s: CashSession) => s.expectedMinor,
  variance: (s: CashSession) => s.varianceMinor ?? 0,
};
export type SessionSortColumn = keyof typeof sessionSort;
export const isSessionSortable = (c: SessionColumn): c is SessionSortColumn => c in sessionSort;

export type DayCloseProblem = 'required' | 'already_open' | 'same_user' | 'invalid' | 'range' | 'state' | 'stale' | 'failed';
const known: readonly string[] = ['required', 'already_open', 'same_user', 'invalid', 'range'];
/** A server refusal ("note:required", "confirm:same_user", "state:invalid", 409 "changed") as a `billing.dayClose.error.<key>` suffix. */
export function dayCloseProblem(code: string | undefined): DayCloseProblem {
  if (code === 'changed') return 'stale';
  if (code === 'state:invalid') return 'state';
  const reason = code?.split(':')[1];
  return reason && known.includes(reason) ? (reason as DayCloseProblem) : 'failed';
}
