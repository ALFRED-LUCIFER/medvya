import { z } from 'zod';

// GET /v1/schedule/metrics (src/Modules/Scheduling/Medvya.Scheduling.Application/IPilotMetrics.cs): counts, medians and rates only.
export const pilotMetricIds = ['CheckInSeconds', 'NoShowPercent', 'DoubleBookings', 'ReminderDeliveryPercent'] as const;
export type PilotMetricId = (typeof pilotMetricIds)[number];
export type Baseline = { metric: PilotMetricId; value: number; method: string; fromDate: string; toDate: string; recordedAt: string };
export type PilotMetrics = {
  from: string;
  to: string;
  checkIn: { returningMedianSeconds: number | null; returningCount: number; medianSeconds: number | null; count: number; targetSeconds: number };
  noShows: { noShows: number; attended: number; percent: number | null };
  doubleBookingRefusals: number;
  baselines: Baseline[];
};

// GET /v1/notifications/delivery (Medvya.Notifications.Application/INotifications.cs): per-channel counts; failures are not shown here.
export type DeliveryReport = { channels: { channel: string; total: number; delivered: number; failed: number; pending: number }[] };

/** The days every number on the report covers: the two weeks a baseline is captured over. */
export const windowDays = 14;

/** Messages delivered out of those with an outcome (still pending ones are left out), in percent; null when none has one yet. */
export function deliveryPercent(report: DeliveryReport): { percent: number | null; delivered: number; settled: number } {
  const delivered = report.channels.reduce((n, c) => n + c.delivered, 0);
  const settled = delivered + report.channels.reduce((n, c) => n + c.failed, 0);
  return { percent: settled === 0 ? null : Math.round((1000 * delivered) / settled) / 10, delivered, settled };
}

/** Seconds over the exit-gate target (0 when met), or null with no returning-patient check-ins yet. */
export const checkInGap = (m: PilotMetrics['checkIn']) =>
  m.returningMedianSeconds === null ? null : Math.max(0, Math.round(m.returningMedianSeconds - m.targetSeconds));

const percentMetrics: readonly PilotMetricId[] = ['NoShowPercent', 'ReminderDeliveryPercent'];

/** The baseline form; the server repeats every check. The value stays text in the form and is sent as a number. */
export const baselineSchema = z.object({
  metric: z.enum(pilotMetricIds),
  value: z.string().trim().regex(/^\d{1,6}$/), // whole seconds, percent or count
  method: z.string().trim().min(1).max(200),
  fromDate: z.iso.date(),
  toDate: z.iso.date(),
})
  .refine(b => !percentMetrics.includes(b.metric) || Number(b.value) <= 100, { path: ['value'] })
  .refine(b => b.fromDate <= b.toDate, { path: ['toDate'] });
export type BaselineInput = z.infer<typeof baselineSchema>;

export const baselineBody = (facilityId: string, b: BaselineInput) => ({ facilityId, ...b, value: Number(b.value), method: b.method.trim() });
