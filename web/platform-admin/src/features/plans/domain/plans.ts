import { z } from 'zod';
import type { MessageKey } from '@medvya/i18n';
import { reasonSchema } from '../../team/domain/team';

export type Country = 'IN' | 'AE';
export type PlanPrice = { country: Country; currency: 'INR' | 'AED'; monthly: number | null; yearly: number | null; assistMonthly: number | null };
/** One plan version (ADR-019). Money is integer minor units (paise, fils); a null limit is unlimited. */
export type PlanVersion = {
  id: string; code: string; version: number; perDoctor: boolean; custom: boolean;
  doctors: number | null; staff: number | null; locations: number | null; assistVisitsPerDoctorMonth: number | null; trialDays: number;
  features: string[]; prices: PlanPrice[]; createdAt: string; createdBy: string | null; reason: string | null;
};
export type Plan = { code: string; current: PlanVersion; history: PlanVersion[] };

/** The server lists every version, by code and newest first; the first of each code is its current version. */
export function groupPlans(versions: PlanVersion[]): Plan[] {
  const byCode = new Map<string, PlanVersion[]>();
  for (const v of versions) byCode.set(v.code, [...(byCode.get(v.code) ?? []), v]);
  return [...byCode].map(([code, history]) => ({ code, current: history[0], history }));
}

export const assistKey = 'ai.ambient_notes';

/**
 * Feature keys the console can name, from the Admin design's feature list. A plan may hold others (shown by key).
 * shortcut: a fixed list; read the code feature list once MDV-P5-041 adds it.
 */
const featureKeys = {
  'ai.ambient_notes': 'platform.plans.feature.ai_ambient_notes',
  'patient_app.booking': 'platform.plans.feature.patient_app_booking',
  'patient_app.trackers': 'platform.plans.feature.patient_app_trackers',
  'auth.sso': 'platform.plans.feature.auth_sso',
  'integrations.his': 'platform.plans.feature.integrations_his',
  'erx.pharmacy': 'platform.plans.feature.erx_pharmacy',
  'abdm.records': 'platform.plans.feature.abdm_records',
  'whatsapp.reminders': 'platform.plans.feature.whatsapp_reminders',
  'billing.upi_autopay': 'platform.plans.feature.billing_upi_autopay',
  'chart.lifestage': 'platform.plans.feature.chart_lifestage',
} as const satisfies Record<string, MessageKey>;
export const knownFeatures = Object.keys(featureKeys);
export const featureLabel = (key: string): MessageKey | null => (featureKeys as Record<string, MessageKey>)[key] ?? null;

const planNames: Record<string, MessageKey> = { solo: 'site.pricing.plan.solo.name', clinic: 'site.pricing.plan.clinic.name', hospital: 'site.pricing.plan.hospital.name' };
export const planName = (code: string): MessageKey | null => planNames[code] ?? null;

/** Column definitions are data: each is its heading's i18n key. The plan identifies a row (MDV-P2-054). */
export const planColumns = ['platform.plans.plan', 'platform.plans.country.IN', 'platform.plans.country.AE', 'platform.plans.limits', 'platform.plans.assist',
  'platform.plans.trial', 'platform.plans.features'] as const;
export const historyColumns = ['platform.plans.version', 'platform.plans.saved', 'platform.plans.by', 'platform.plans.reason',
  'platform.plans.country.IN', 'platform.plans.country.AE'] as const;

// Amounts are typed in rupees or dirhams (up to 2 decimals) and sent in minor units; parsed as text, so no float rounding creeps in.
const money = z.string().trim().regex(/^(\d{1,9}(\.\d{1,2})?)?$/, 'money');
const count = z.string().trim().regex(/^(\d{1,6})?$/, 'whole');

export const planForm = z.object({
  prices: z.array(z.object({ country: z.enum(['IN', 'AE']), monthly: money, yearly: money, assistMonthly: money })),
  doctors: count.refine(v => v !== '0', 'atLeastOne'),
  staff: count,
  locations: count.refine(v => v !== '0', 'atLeastOne'),
  assistVisitsPerDoctorMonth: count,
  trialDays: z.string().trim().regex(/^\d{1,2}$/, 'whole').refine(v => Number(v) <= 90, 'trialDays'),
  features: z.array(z.string()),
  reason: reasonSchema,
});
export type PlanForm = z.infer<typeof planForm>;

export function toMinor(text: string): number | null {
  if (text.trim() === '') return null;
  const [whole, fraction = ''] = text.trim().split('.');
  return Number(whole) * 100 + Number(fraction.padEnd(2, '0'));
}

export const fromMinor = (minor: number | null) =>
  minor === null ? '' : minor % 100 === 0 ? String(minor / 100) : `${Math.floor(minor / 100)}.${String(minor % 100).padStart(2, '0')}`;

const toCount = (text: string) => (text.trim() === '' ? null : Number(text));
const fromCount = (n: number | null) => (n === null ? '' : String(n));

export const formFor = (v: PlanVersion): PlanForm => ({
  prices: v.prices.map(p => ({ country: p.country, monthly: fromMinor(p.monthly), yearly: fromMinor(p.yearly), assistMonthly: fromMinor(p.assistMonthly) })),
  doctors: fromCount(v.doctors), staff: fromCount(v.staff), locations: fromCount(v.locations),
  assistVisitsPerDoctorMonth: fromCount(v.assistVisitsPerDoctorMonth), trialDays: String(v.trialDays), features: [...v.features], reason: '',
});

/** The request body for the next version: the whole plan, and the version the edit started from (the server refuses a stale one). */
export const editFor = (base: PlanVersion, f: PlanForm) => ({
  baseVersion: base.version,
  prices: f.prices.map(p => ({ country: p.country, monthly: toMinor(p.monthly), yearly: toMinor(p.yearly), assistMonthly: toMinor(p.assistMonthly) })),
  doctors: toCount(f.doctors), staff: toCount(f.staff), locations: toCount(f.locations), assistVisitsPerDoctorMonth: toCount(f.assistVisitsPerDoctorMonth),
  trialDays: Number(f.trialDays), features: f.features, reason: f.reason,
});

/** Features the edit form offers: the known ones, then any other key the plan already holds, so saving never drops one silently. */
export const featureChoices = (v: PlanVersion) => [...knownFeatures, ...v.features.filter(f => !knownFeatures.includes(f))];
