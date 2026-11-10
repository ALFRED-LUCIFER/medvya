import type { Page } from '@playwright/test';
import { mockOrders } from './orders';

// The draft prescription of the open consultation (MDV-P2-015), mocked in the shapes the server sends (IPrescriptions.cs). The readable
// instruction is the server's (proven per language in PrescriptionTests); here it is a stand-in built from the codes, one per print language.
export type RxLine = {
  id: string | null; kind: 'Prescribe' | 'Discontinue'; source: string; formularyItemId: string | null; medicine: string; strength: string | null; form: string | null;
  freeText: boolean; included: boolean; dose: string | null; doseUnit: string | null; route: string | null; frequency: string | null; timing: string | null;
  durationDays: number | null; quantity: number | null; instructions: string | null; reason: string | null; sig: Record<string, string>;
};
type Sig = Pick<RxLine, 'dose' | 'doseUnit' | 'route' | 'frequency' | 'timing' | 'durationDays' | 'quantity' | 'instructions'>;

const blankSig: Sig = { dose: null, doseUnit: null, route: null, frequency: null, timing: null, durationDays: null, quantity: null, instructions: null };
const sigOf = (s: Sig) => {
  const codes = [s.dose, s.doseUnit, s.route, s.frequency, s.timing, s.durationDays && `${s.durationDays}d`].filter(Boolean).join(' ');
  return codes ? { en: `Take ${codes}.`, hi: `${codes} लें।`, ar: `تناول ${codes}.` } : { en: '', hi: '', ar: '' };
};
export const line = (id: string, medicine: string, more: Partial<RxLine> = {}): RxLine => ({
  id, kind: 'Prescribe', source: 'AddedByYou', formularyItemId: 'fi-metformin', medicine, strength: '500 mg', form: 'Tablet', freeText: false, included: true,
  ...blankSig, reason: null, sig: sigOf(blankSig), ...more,
});
export const stopLine = (medicine: string, reason: string): RxLine => ({
  ...line('', medicine), id: null, kind: 'Discontinue', source: 'ChartReconciliation', formularyItemId: null, strength: null, form: null, reason,
  sig: { en: 'Stop this medicine.', hi: 'यह दवा बंद करें।', ar: 'أوقف هذا الدواء.' },
});

// The safety checks (MDV-P2-016), in RxCheckView's shape. Which alert fires for which conflict is proven in SafetyRulesTests and PrescriptionCheckTests.
export type RxChecks = {
  ruleSet: string; ruleSetVersion: string; allergies: 'NotRecorded' | 'NoKnownAllergies' | 'Listed'; alerts: Record<string, unknown>[];
  notChecked: { id: string; medicine: string; onChart: boolean; reason: string }[];
  overrides: { key: string; reason: string; ruleSetVersion: string; overriddenBy: string; overriddenAt: string }[]; unresolved: number; checkedAt: string;
};
export const clearChecks = (more: Partial<RxChecks> = {}): RxChecks => ({
  ruleSet: 'medvya-curated', ruleSetVersion: '2026.11-draft-1', allergies: 'NoKnownAllergies', alerts: [], notChecked: [], overrides: [], unresolved: 0,
  checkedAt: '2026-10-19T04:30:00Z', ...more,
});
export const penicillinAlert = (itemId: string) => ({
  key: `allergy:${itemId}:al-1`, kind: 'Allergy', severity: 'Severe', itemId, medicine: 'Amoxicillin', other: null, otherOnChart: false, code: 'penicillins',
  substance: 'Penicillin', allergyStatus: 'Confirmed', dailyMicrograms: null, maxDailyMicrograms: null,
});

export async function mockPrescription(page: Page, opts: { locale?: string; ended?: boolean; allowed?: boolean; lines?: RxLine[]; checks?: RxChecks } = {}) {
  await mockOrders(page, { locale: opts.locale, ended: opts.ended });
  const lines: RxLine[] = [...(opts.lines ?? [])];
  const calls = { added: [] as unknown[], saved: [] as unknown[], included: [] as unknown[], searched: [] as string[], checked: 0, overrides: [] as unknown[] };
  let checks = opts.checks ?? clearChecks();
  await page.route('**/v1/encounters/e-1/prescription/checks', r => { calls.checked++; return r.fulfill({ json: checks }); });
  await page.route('**/v1/encounters/e-1/prescription/overrides', r => {
    const body = r.request().postDataJSON() as { key: string; reason: string };
    calls.overrides.push(body);
    checks = { ...checks, unresolved: checks.unresolved - 1, overrides: [...checks.overrides, { ...body, ruleSetVersion: checks.ruleSetVersion, overriddenBy: 'u-rao', overriddenAt: checks.checkedAt }] };
    return r.fulfill({ json: checks });
  });
  const answer = () => [...lines.filter(l => l.kind === 'Prescribe'), ...lines.filter(l => l.kind === 'Discontinue')];
  await page.route('**/v1/encounters/e-1/prescription', r =>
    opts.allowed === false ? r.fulfill({ status: 403, json: { code: 'no_access', requestPending: false } }) : r.fulfill({ json: answer() }));
  await page.route('**/v1/encounters/e-1/prescription/items', r => {
    const body = r.request().postDataJSON() as { formularyItemId?: string; medicine?: string };
    calls.added.push(body);
    lines.push(body.formularyItemId
      ? line(`rx-${lines.length + 1}`, 'Metformin')
      : line(`rx-${lines.length + 1}`, body.medicine!, { formularyItemId: null, strength: null, form: null, freeText: true }));
    return r.fulfill({ json: answer() });
  });
  await page.route('**/v1/prescription-items/*', r => {
    const id = new URL(r.request().url()).pathname.split('/')[3]!;
    const body = r.request().postDataJSON() as Sig;
    calls.saved.push(body);
    Object.assign(lines.find(l => l.id === id)!, body, { sig: sigOf(body) });
    return r.fulfill({ json: answer() });
  });
  await page.route('**/v1/prescription-items/*/included', r => {
    const id = new URL(r.request().url()).pathname.split('/')[3]!;
    const body = r.request().postDataJSON() as { included: boolean };
    calls.included.push(body);
    lines.find(l => l.id === id)!.included = body.included;
    return r.fulfill({ json: answer() });
  });
  await page.route('**/v1/formulary?*', r => {
    calls.searched.push(new URL(r.request().url()).searchParams.get('q') ?? '');
    return r.fulfill({ json: [{ id: 'fi-metformin', generic: 'Metformin', strength: '500 mg', form: 'Tablet', brands: ['Glycomet'], nationalCode: null, regClass: 'ScheduleH',
      printRequirements: ['warning_schedule_h'], atc: 'A10BA02', defaultSig: '1 tablet twice a day with meals', source: 'Starter', active: true }] });
  });
  return { lines, calls };
}
