import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';
import { mockNurse } from './support/nurse';

// MDV-P2-007 (N-01): the nurse opens the intake from the queue, an impossible value is refused with words, an out-of-range one is flagged in
// words and must be re-measured or kept before the handoff, an escalation needs a reason, and the handoff returns to the queue. The server
// rules themselves are proven on PostgreSQL (IntakeTests, IntakeEndpointTests).
test('the nurse works the intake from the queue to the handoff', async ({ page }) => {
  const { calls } = await mockNurse(page);
  await page.goto('/nurse');
  await expect(page.getByRole('heading', { name: 'Nurse intake queue' })).toBeVisible();
  await page.getByRole('link', { name: 'Open intake for Meera Iyer (synthetic)' }).click();
  await expect(page).toHaveURL(/\/nurse\/a1$/);
  await expect(page.getByRole('region', { name: 'Patient identity' }).getByRole('heading', { name: 'Meera Iyer (synthetic)' })).toBeVisible();

  const handOff = page.getByRole('button', { name: 'Hand off · Ready for doctor' });
  await page.getByLabel('Chief complaint, in the patient\'s words').fill('Chest tightness on stairs');

  // An impossible value is refused in words and nothing is flagged.
  await page.getByLabel('SpO₂').fill('130');
  await page.getByRole('button', { name: 'Save vitals' }).click();
  await expect(page.getByText('This value is not possible. Check the reading and measure again.')).toBeVisible();

  // An out-of-range value is flagged in words, with a re-measure and a keep, and the handoff waits.
  await page.getByLabel('SpO₂').fill('');
  await page.getByLabel('BP systolic').fill('168');
  await page.getByLabel('Weight').fill('72.4');
  await page.getByRole('button', { name: 'Save vitals' }).click();
  const flag = page.getByText('High · re-check');
  await expect(flag).toBeVisible();
  // Numbers are isolated left-to-right, so these match around the invisible marks.
  await expect(page.getByText(/\+2\.4.*kg.*since 02 Oct 2026 · re-check/)).toBeVisible();
  await expect(page.getByText(/Previous.*70.*kg.* · 02 Oct 2026/)).toBeVisible();
  expect(calls.saves.at(-1)).toEqual({ chiefComplaint: 'Chest tightness on stairs', vitals: [{ kind: 'SystolicBp', value: 168 }, { kind: 'Weight', value: 72.4 }] });
  await expect(handOff).toBeDisabled();
  await expect(page.getByText('Re-measure or keep each flagged reading to hand off.')).toBeVisible();

  await page.getByRole('button', { name: 'Re-measure BP systolic' }).click();
  await expect(page.getByRole('textbox', { name: 'BP systolic' })).toBeFocused();
  await page.getByRole('button', { name: 'Keep this value: BP systolic' }).click();
  await page.getByRole('button', { name: 'Keep this value: Weight' }).click();
  await expect(page.getByText('Kept as measured after a re-check')).toHaveCount(2);
  expect(calls.keeps).toEqual(['SystolicBp', 'Weight']);

  // Escalating needs a reason.
  await page.getByRole('button', { name: 'Escalate to doctor' }).click();
  await page.getByRole('button', { name: 'Send urgent item' }).click();
  await expect(page.getByText('Give a reason so the doctor knows why.')).toBeVisible();
  expect(calls.escalations).toEqual([]);
  await page.getByLabel('Reason for the doctor').fill('Chest tightness with BP 168 (synthetic)');
  await page.getByRole('button', { name: 'Send urgent item' }).click();
  await expect(page.getByText('Escalated: the treating doctor has an urgent item for this patient.')).toBeVisible();
  expect(calls.escalations).toEqual([{ reason: 'Chest tightness with BP 168 (synthetic)' }]);

  // No allergy is recorded, and "no known allergies" is never assumed: the handoff waits for the nurse to confirm it.
  await expect(handOff).toBeDisabled();
  await expect(page.getByText('Confirm allergy status to hand off.')).toBeVisible();
  await page.getByRole('button', { name: 'Confirm: no known allergies' }).click();
  await expect(page.getByText('No known allergies, confirmed by the nurse.')).toBeVisible();
  await expect(handOff).toBeEnabled();
  await handOff.click();
  await expect(page).toHaveURL(/\/nurse$/);
  expect(calls.handoffs).toBe(1);
  await expect(page.getByRole('heading', { name: 'No one is with the nurse' })).toBeVisible();
});

test('reception does not move a With nurse card around the nurse', async ({ page }) => {
  await mockNurse(page);
  await page.goto('/front-desk');
  const card = page.getByRole('article', { name: 'Meera Iyer (synthetic)' });
  await expect(card.getByRole('button', { name: 'Intake done' })).toHaveCount(0);
  await card.getByRole('link', { name: 'Open intake for Meera Iyer (synthetic)' }).click();
  await expect(page).toHaveURL(/\/nurse\/a1$/);
});

// MDV-P2-008: every allergy needs a status before the handoff; "no known allergies" is explicit; medicines are patient-reported.
test('an allergy without a status blocks the handoff until the nurse sets one', async ({ page }) => {
  const { calls } = await mockNurse(page, 'en', { unreviewed: true });
  await page.goto('/nurse/a1');
  const allergies = page.getByRole('region', { name: 'Allergies' });
  await expect(allergies.getByText('Penicillin (synthetic)', { exact: true })).toBeVisible();
  await expect(allergies.getByText('Status not set')).toBeVisible(); // words and an icon, not colour
  await expect(allergies.getByText('Patient reported', { exact: true }).first()).toBeVisible();
  const handOff = page.getByRole('button', { name: 'Hand off · Ready for doctor' });
  await expect(handOff).toBeDisabled();
  await expect(page.getByText('Confirm allergy status to hand off.')).toBeVisible();

  await allergies.getByRole('combobox', { name: 'Status of Penicillin (synthetic)' }).click();
  await page.getByRole('option', { name: 'Patient reported' }).click();
  expect(calls.statuses).toEqual([{ id: 'al-1', status: 'PatientReported' }]);
  await expect(allergies.getByText('Status not set')).toHaveCount(0);
  await expect(handOff).toBeEnabled();
  await handOff.click();
  await expect(page).toHaveURL(/\/nurse$/);
  expect(calls.handoffs).toBe(1);
});

test('the nurse adds an allergy with its reaction and a medicine the patient could not name', async ({ page }) => {
  const { calls } = await mockNurse(page);
  await page.goto('/nurse/a1');
  const allergies = page.getByRole('region', { name: 'Allergies' });
  await expect(allergies.getByText('No allergies are recorded for this patient.')).toBeVisible();
  await allergies.getByRole('button', { name: 'Add allergy' }).click(); // nothing typed: refused in words
  await expect(allergies.getByText('Say what the patient is allergic to.')).toBeVisible();
  await allergies.getByLabel('Allergic to').fill('Peanuts (synthetic)');
  await allergies.getByLabel('Reaction').fill('Hives');
  await allergies.getByRole('button', { name: 'Add allergy' }).click();
  await expect(allergies.getByText('Peanuts (synthetic) · Hives')).toBeVisible();
  expect(calls.allergies).toEqual([{ substance: 'Peanuts (synthetic)', reaction: 'Hives', status: 'PatientReported' }]);
  await expect(allergies.getByText('Patient reported', { exact: true }).first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Hand off · Ready for doctor' })).toBeEnabled(); // an allergy with a status needs no "none"

  const medicines = page.getByRole('region', { name: 'Medicines the patient takes' });
  await medicines.getByLabel('Medicine name, as the patient says it').fill('a white tablet for sugar');
  await medicines.getByRole('switch', { name: 'Needs clarification' }).click();
  await medicines.getByRole('button', { name: 'Add medicine' }).click();
  await expect(medicines.getByText('a white tablet for sugar')).toBeVisible();
  await expect(medicines.getByText('Needs clarification').first()).toBeVisible();
  expect(calls.medicines).toEqual([{ name: 'a white tablet for sugar', dose: '', frequency: '', needsClarification: true }]);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the intake (${locale}); tablet targets are 44 px`, async ({ page }) => {
    await page.setViewportSize({ width: 820, height: 1180 });
    await mockNurse(page, locale, { unreviewed: true });
    await page.goto('/nurse/a1');
    await page.getByRole('textbox').nth(1).fill('168'); // BP systolic
    await page.getByRole('button').filter({ hasText: /^(Save vitals|احفظ العلامات الحيوية)$/ }).click();
    await expect(page.getByText(locale === 'ar' ? /مرتفع · أعد القياس/ : 'High · re-check')).toBeVisible();
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    for (const target of await page.getByRole('main').getByRole('button').all()) expect((await target.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    for (const target of await page.getByRole('main').getByRole('textbox').all()) expect((await target.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
        .map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);
  });
