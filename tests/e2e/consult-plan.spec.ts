import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { mockNote } from './support/note';
import { line, mockPrescription, type RxLine } from './support/prescription';

// MDV-P2-043: the Plan section of the one-page consultation. CI has no API: the note, reconciliation, prescription, orders, advice, follow-up and
// referrals are mocked in the shapes the server sends. The rules (taper steps and end date, the order toggle that never takes out an ordered test,
// the follow-up on the desk's waitlist, the advice frozen with the prescription) are proven on PostgreSQL in MedicationReconciliationTests,
// OrderTests, FollowUpTests and RxAuthorisationTests.
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const doctor = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [] as string[],
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'Full', ClinicalNotes: 'Sign', PrescriptionsOrders: 'Sign' } },
};
const at = '2026-10-19T04:35:00Z';
const side = (dose: string | null) => ({ dose, frequency: null, state: 'Active', needsClarification: false });
type Decision = { kind: string; newDose: string | null; reason: string | null; decidedAt: string; fromDose: string | null; steps: { dose: string; days: number }[] | null; endsOn: string | null };

async function open(page: Page, locale = 'en', path = '/consult/e-1') {
  const calls = { decisions: [] as Record<string, unknown>[], advice: [] as unknown[], followUps: [] as unknown[] };
  const rx = await mockPrescription(page, { locale, lines: [line('rx-1', 'Metformin')] });
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
  await mockNote(page, { version: 1, sections: { chiefComplaint: 'Thirst (synthetic)', plan: 'Review in a month' } });
  const rows = [
    { key: 'atorvastatin', name: 'Atorvastatin', chart: side('20 mg'), patient: null, other: null, conflict: null, allowed: ['Keep', 'Change', 'Taper', 'Stop'], decision: null as Decision | null },
    { key: 'telmisartan', name: 'Telmisartan', chart: side('40 mg'), patient: null, other: null, conflict: null, allowed: ['Keep', 'Change', 'Taper', 'Stop'], decision: null as Decision | null },
  ];
  const view = () => ({ encounterId: 'e-1', patientId: 'p-1', rows, decided: rows.filter(r => r.decision).length, total: rows.length, disagree: 0, confirmedAt: null });
  await page.route('**/v1/encounters/e-1/medicines/reconciliation', r => r.fulfill({ json: view() }));
  await page.route('**/v1/encounters/e-1/medicines/reconciliation/decisions', r => {
    const body = r.request().postDataJSON() as { key: string; kind: string; newDose: string; reason: string; steps?: { dose: string; days: number }[] };
    calls.decisions.push(body);
    rows.find(x => x.key === body.key)!.decision = { kind: body.kind, newDose: body.kind === 'Change' ? body.newDose : null, reason: body.reason || null, decidedAt: at,
      fromDose: '20 mg', steps: body.steps ?? null, endsOn: body.kind === 'Taper' ? '2026-11-01' : null };
    return r.fulfill({ json: view() });
  });
  // The prescription as the server builds it: the composer's lines, then a taper line for every taper decision.
  const tapers = (): RxLine[] => rows.filter(x => x.decision?.kind === 'Taper').map(x => ({ ...line('', x.name), id: null, kind: 'Taper' as RxLine['kind'], source: 'ChartReconciliation',
    formularyItemId: null, strength: null, form: null, reason: x.decision!.reason, sig: { en: 'Reduce the dose step by step: 10 mg for 7 days, then 5 mg for 7 days. Last dose on 01 Nov 2026.' } }));
  await page.route('**/v1/encounters/e-1/prescription', r => r.fulfill({ json: [line('rx-1', 'Metformin'), ...tapers()] }));
  let advice = { text: null as string | null, patientLanguage: 'ta', inPatientLanguage: false };
  await page.route('**/v1/encounters/e-1/prescription/advice', r => {
    if (r.request().method() === 'GET') return r.fulfill({ json: advice });
    calls.advice.push(r.request().postDataJSON());
    advice = { ...advice, text: (r.request().postDataJSON() as { text: string | null }).text };
    return r.fulfill({ json: advice });
  });
  let followUp = { period: null as string | null, dueOn: null as string | null, state: null as string | null };
  await page.route('**/v1/encounters/e-1/follow-up', r => {
    if (r.request().method() === 'GET') return r.fulfill({ json: followUp });
    const { period } = r.request().postDataJSON() as { period: string | null };
    calls.followUps.push(period);
    followUp = period ? { period, dueOn: period === 'P1M' ? '2026-11-19' : '2026-11-02', state: 'Waiting' } : { period: null, dueOn: null, state: null };
    return r.fulfill({ json: followUp });
  });
  await page.route('**/v1/patients/p-1/referrals', r => r.fulfill({ json: [] }));
  await page.goto(path);
  return { calls, rx };
}
const plan = (page: Page) => page.locator('div:has(> #studio-plan)');
const axe = async (page: Page) => {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: { target: string[] }[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.map(n => n.target.join(' ')).join(', ')})`));
};

test('a chart medicine is tapered in steps from the plan; the taper is a line of the prescription', async ({ page }) => {
  const { calls } = await open(page);
  const medicines = plan(page).getByRole('list', { name: 'Medicines', exact: true });
  const statin = medicines.getByRole('listitem').filter({ hasText: 'Atorvastatin' });
  await statin.getByRole('button', { name: 'Taper' }).click();
  await statin.getByLabel('Step 1: dose').fill('10 mg');
  await statin.getByLabel('Step 1: days').fill('7');
  await statin.getByLabel('Step 2: dose').fill('5 mg');
  await statin.getByLabel('Step 2: days').fill('x');
  await statin.getByLabel('Reason').fill('Leg aches (synthetic)');
  await statin.getByRole('button', { name: 'Save decision' }).click();
  await expect(statin.getByRole('alert')).toContainText('Give each step a dose and a whole number of days'); // checked before the server is asked
  expect(calls.decisions).toHaveLength(0);
  await statin.getByLabel('Step 2: days').fill('7');
  await statin.getByRole('button', { name: 'Save decision' }).click();
  expect(calls.decisions.at(-1)).toEqual({ key: 'atorvastatin', kind: 'Taper', newDose: '', reason: 'Leg aches (synthetic)', steps: [{ dose: '10 mg', days: 7 }, { dose: '5 mg', days: 7 }] });
  // Doses are isolated left to right (U+2066 … U+2069), so they read correctly in Arabic too.
  await expect(statin).toContainText('Decided: Taper · \u206610 mg\u2069 for 7 days, then \u20665 mg\u2069 for 7 days · last dose on Sunday, 1 November 2026 · Leg aches (synthetic)');
  const rx = page.getByRole('region', { name: 'Prescription', exact: true }); // the composer, under the note
  await expect(rx.getByRole('list', { name: 'Medicines to stop, change or taper' })).toContainText('Taper');
  await expect(rx).toContainText('Last dose on 01 Nov 2026.');
});

test('Add medicine opens the side panel from the plan', async ({ page }) => {
  await open(page);
  await plan(page).getByRole('button', { name: 'Add medicine' }).click();
  await expect(page.getByRole('dialog', { name: 'Add medicine' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(plan(page).getByRole('button', { name: 'Add medicine' })).toBeFocused();
});

test('a test toggled on is one draft; off switches the draft off; on again re-includes it', async ({ page }) => {
  const { rx } = await open(page);
  const tests = plan(page).getByRole('list', { name: 'Tests to order' });
  await tests.getByRole('button', { name: 'HbA1c' }).click();
  await expect(tests.getByRole('button', { name: 'HbA1c' })).toHaveAttribute('aria-pressed', 'true');
  await tests.getByRole('button', { name: 'HbA1c' }).click();
  await expect(tests.getByRole('button', { name: 'HbA1c' })).toHaveAttribute('aria-pressed', 'false');
  await tests.getByRole('button', { name: 'HbA1c' }).click();
  await expect(tests.getByRole('button', { name: 'HbA1c' })).toHaveAttribute('aria-pressed', 'true');
  expect(rx.orderCalls.added).toEqual([{ kind: 'Lab', name: 'HbA1c', note: null }]); // created once
  expect(rx.orderCalls.included).toEqual([{ included: false }, { included: true }]);
  await expect(tests.getByRole('button', { name: 'Fasting glucose' })).toHaveAttribute('aria-pressed', 'false');
});

test('the advice is written once; for a Tamil speaker it says it prints and is sent in English', async ({ page }) => {
  const { calls } = await open(page);
  await expect(plan(page)).toContainText('The patient\'s language is Tamil. There is no reviewed translation yet, so it prints and is sent in English with a note.');
  await plan(page).getByLabel('Advice for the patient').fill('Walk 30 minutes a day (synthetic).');
  await plan(page).getByRole('button', { name: 'Save advice' }).click();
  await expect(plan(page).getByRole('status').filter({ hasText: 'Advice saved' })).toBeVisible();
  expect(calls.advice).toEqual([{ text: 'Walk 30 minutes a day (synthetic).' }]);
});

test('the follow-up is one choice the front desk books from its due day; picking it again takes it off', async ({ page }) => {
  const { calls } = await open(page);
  const fu = plan(page).getByRole('group', { name: 'Follow-up' });
  await fu.getByRole('button', { name: '1 month' }).click();
  await expect(fu.getByRole('button', { name: '1 month' })).toHaveAttribute('aria-pressed', 'true');
  await expect(plan(page).getByRole('status').filter({ hasText: 'The front desk will book it from Thursday, 19 November 2026.' })).toBeVisible();
  await fu.getByRole('button', { name: '1 month' }).click();
  await expect(plan(page).getByRole('status').filter({ hasText: 'No follow-up set.' })).toBeVisible();
  expect(calls.followUps).toEqual(['P1M', null]);
});

test('Referral moves focus to the visit\'s referrals, where the letter is written', async ({ page }) => {
  await open(page);
  await expect(page.getByRole('region', { name: 'Referrals' })).toBeHidden(); // closed until used (MDV audit P1-04)
  await plan(page).getByRole('link', { name: 'Referral' }).click();
  await expect(page.getByRole('region', { name: 'Referrals' })).toBeVisible(); // the link opens it
  await expect(page.getByRole('heading', { name: 'Referrals' })).toBeFocused();
});

test('"Plan change" from the older adult medicine review lands on the Plan with focus', async ({ page }) => {
  await open(page, 'en', '/consult/e-1#studio-plan');
  await expect(page.getByRole('heading', { name: /^6 · / })).toBeFocused();
});

for (const locale of ['en', 'ar'])
  test(`axe finds no violations in the plan (${locale}); right to left in Arabic; targets are 44 px`, async ({ page }) => {
    await open(page, locale);
    const ar = locale === 'ar';
    await expect(plan(page).getByRole('button', { name: ar ? 'تخفيض تدريجي' : 'Taper' }).first()).toBeVisible();
    if (ar) await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await plan(page).getByRole('button', { name: ar ? 'تخفيض تدريجي' : 'Taper' }).first().click(); // the taper form open too
    for (const target of await plan(page).locator('button:not([role=switch]):not([role=combobox]), a, input, textarea').all()) expect((await target.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    expect(await axe(page)).toEqual([]);
  });
