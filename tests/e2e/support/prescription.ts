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

// MDV-P2-017: the authorisation (RxAuthorisationState), the print view (RxPrintView) and the public verify answer (RxVerifyView), in the server's shapes.
export const authorised = (versionNo = 1) => ({
  versionNo, number: 'RX-3F9A21C7', authorisedBy: 'u-rao', authorisedAt: '2026-10-19T04:50:00Z', contentHash: 'a'.repeat(64), lines: 1,
});
// A 29-module stand-in for the QR code (the real one is QRCoder's, proven in RxAuthorisationTests).
const qr = Array.from({ length: 29 }, (_, y) => Array.from({ length: 29 }, (_, x) => ((x * 7 + y * 3) % 5 === 0 && x > 3 && y > 3 && x < 25 && y < 25 ? '1' : '0')).join(''));
export const printView = () => ({
  authorisation: authorised(), clinic: 'Iyer Family Clinic · Adyar', timeZone: 'Asia/Kolkata', doctor: 'Dr. Arjun Rao (synthetic)', qualifications: 'MBBS, MD (General Medicine)',
  licenceNumber: '84213', licenceAuthority: 'Tamil Nadu Medical Council',
  patient: { fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', sexAtBirth: 'Female' }, allergies: 'NoKnownAllergies', allergySubstances: [],
  lines: [
    { discontinue: false, medicine: 'Metformin', strength: '500 mg', form: 'Tablet', printRequirements: ['warning_schedule_h'], reason: null, sig: {
      en: 'Take 1 tablet by mouth twice a day after food for 5 days. Quantity: 10.', hi: '1 गोली मुँह से दिन में दो बार खाने के बाद 5 दिन तक लें। मात्रा: 10।',
      ta: '1 மாத்திரை வாய் வழியாக தினமும் இரண்டு முறை உணவுக்குப் பின் 5 நாட்களுக்கு எடுத்துக்கொள்ளவும். அளவு: 10.',
      ar: 'تناول 1 قرص عن طريق الفم مرتين يوميًا بعد الأكل لمدة 5 أيام. الكمية: 10.' } },
    { discontinue: true, medicine: 'Atorvastatin', strength: null, form: null, printRequirements: [], reason: 'Muscle pain (synthetic)', sig: { en: 'Stop this medicine.' } },
  ],
  verifyUrl: 'https://clinic.test/app/verify/rx/synthetic-token', qr, layoutVersion: 1,
});

type AuthState = { authorised: ReturnType<typeof authorised> | null; changed: boolean };

export async function mockPrescription(page: Page, opts: {
  locale?: string; ended?: boolean; allowed?: boolean; lines?: RxLine[]; checks?: RxChecks; authorisation?: AuthState; reauthFirst?: boolean;
} = {}) {
  const ordered = await mockOrders(page, { locale: opts.locale, ended: opts.ended });
  const lines: RxLine[] = [...(opts.lines ?? [])];
  const calls = { added: [] as unknown[], saved: [] as unknown[], included: [] as unknown[], searched: [] as string[], checked: 0, overrides: [] as unknown[], authorised: 0, approved: 0 };
  let state: AuthState = opts.authorisation ?? { authorised: null, changed: false };
  await page.route('**/v1/encounters/e-1/prescription/authorisation', r => {
    if (r.request().method() === 'GET') return r.fulfill({ json: state });
    calls.authorised++;
    if (opts.reauthFirst && calls.authorised === 1) return r.fulfill({ status: 403, json: { code: 'reauth_required' } });
    state = { authorised: authorised((state.authorised?.versionNo ?? 0) + 1), changed: false };
    return r.fulfill({ json: state });
  });
  await page.route('**/v1/encounters/e-1/prescription/print', r =>
    state.authorised && !state.changed ? r.fulfill({ json: printView() }) : r.fulfill({ status: 409, json: { code: state.authorised ? 'changed' : 'not_authorised' } }));
  await page.route('**/v1/encounters/e-1/prescription/shares', r => r.fulfill({ json: [] })); // MDV-P2-020; support/share.ts mocks the share flow
  let layout = { layoutVersion: 1, approvedAt: null as string | null };
  await page.route('**/v1/prescription-layout', r => r.fulfill({ json: layout }));
  await page.route('**/v1/prescription-layout/approval', r => { calls.approved++; layout = { ...layout, approvedAt: '2026-10-19T04:55:00Z' }; return r.fulfill({ json: layout }); });
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
  return { lines, calls, orders: ordered.orders, orderCalls: ordered.calls };
}
