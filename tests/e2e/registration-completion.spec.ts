import { createRequire } from 'node:module';
import { expect, test, type Page, type Request } from '@playwright/test';

// MDV-P1-015: the completion task for a quick-registered patient, from the record page. CI has no API: /v1/me/context and
// /v1/patients/* are mocked with the synthetic cast; the facility scope and audit are covered by PatientEndpointTests.
const desk = (capabilities: Record<string, string>) => ({
  user: { id: 'u-joseph', displayName: 'A. Joseph' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'Receptionist' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Receptionist', flags: [], country: 'IN', capabilities },
});
const full = { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'None', ClinicalNotes: 'None' };
const id = '5e1d0001-0004-4000-8000-000000000131';
const leela = {
  id, mrn: '001131', fullName: 'Leela Raman (synthetic)', arabicName: null, dateOfBirth: '1993-04-02', dateOfBirthEstimated: false, ageYears: 33,
  sexAtBirth: 'Female', genderIdentity: null, preferredLanguage: 'Tamil, English', nationality: null, mobile: '+919000000131',
  alternatePhone: null, email: null, status: 'Incomplete', registeredOn: '2026-10-25', address: null, identifiers: [], payer: null,
  emergencyContact: null, guardian: null, version: 7,
};

async function open(page: Page, { capabilities = full as Record<string, string>, locale = 'en' } = {}) {
  const saves: Request[] = [];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: desk(capabilities) }));
  await page.route(`**/v1/patients/${id}`, r => r.fulfill({ json: leela }));
  await page.route(`**/v1/patients/${id}/consents`, r => r.fulfill({ json: { policyVersion: '2026-11-draft', decisions: [] } })); // the record page's Consents section (MDV-P1-019): unmocked, it fails and the retry reshapes the page after networkidle
  await page.route(`**/v1/patients/${id}/completion`, r => { saves.push(r.request()); return r.fulfill({ json: { status: 'Complete' } }); });
  await page.goto(`/patients/directory/${id}`);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  return saves;
}

test('the record shows the completion task; completing it sends address and payer only and closes the task', async ({ page }) => {
  const saves = await open(page);
  const task = page.getByRole('region', { name: 'Registration to complete' });
  await expect(task).toContainText('Missing: address, payer, and emergency contact. Add them while the patient is at the desk.');
  await expect(page.getByText('Tamil, English')).toBeVisible(); // free text from before language codes is shown as entered
  await task.getByRole('link', { name: 'Complete registration' }).click();
  await expect(page).toHaveURL(`/patients/directory/${id}/complete`);

  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Add the address, the payer or the emergency contact.')).toBeVisible();
  expect(saves).toHaveLength(0);

  await page.getByLabel('House / flat, building').fill('4 Synthetic Street');
  await page.getByLabel('City / town / village').fill('Chennai');
  await page.getByRole('combobox', { name: 'State / UT' }).click();
  await page.getByRole('option', { name: 'Tamil Nadu', exact: true }).click();
  await page.getByLabel('PIN code').fill('600020');
  await page.getByRole('combobox', { name: 'Payment type' }).click();
  await page.getByRole('option', { name: 'Self-pay', exact: true }).click();
  await page.getByLabel('Emergency contact name').fill('Mohan Raman');
  await page.getByRole('combobox', { name: 'Relationship', exact: true }).click();
  await page.getByRole('option', { name: 'Sibling', exact: true }).click();
  await page.getByLabel('Emergency contact phone').fill('+91 90000 00132');
  await page.getByRole('button', { name: 'Save' }).click();

  await expect(page.getByText('Registration complete. The completion task is closed.')).toBeVisible();
  expect(saves).toHaveLength(1);
  expect(new URL(saves[0]!.url()).search).toBe('');
  expect(saves[0]!.postDataJSON()).toEqual({
    address: { line1: '4 Synthetic Street', line2: null, city: 'Chennai', district: null, region: 'Tamil Nadu', postalCode: '600020', makani: null, poBox: null },
    payer: { kind: 'SelfPay', payerName: null, plan: null, memberNumber: null, validTo: null, network: null },
    emergencyContact: { name: 'Mohan Raman', relationship: 'Sibling', phone: '+91 90000 00132' },
    version: 7, // #889: the version the record was read at
  });
  await page.getByRole('link', { name: 'Back to the record' }).click();
  await expect(page).toHaveURL(`/patients/directory/${id}`);
});

test('a role that may only read patients sees the tag but not the completion action', async ({ page }) => {
  await open(page, { capabilities: { RegisterBookPatients: 'Read' } });
  await expect(page.getByText('Incomplete registration').first()).toBeVisible();
  await expect(page.getByRole('link', { name: 'Complete registration' })).toHaveCount(0);
});

test('another facility\'s patient is not found here, and nothing is shown to complete', async ({ page }) => {
  await page.route('**/v1/me/context', r => r.fulfill({ json: desk(full) }));
  await page.route(`**/v1/patients/${id}`, r => r.fulfill({ json: leela }));
  await page.route(`**/v1/patients/${id}/completion`, r => r.fulfill({ status: 404, json: {} }));
  await page.goto(`/patients/directory/${id}/complete`);
  await page.getByLabel('House / flat, building').fill('4 Synthetic Street');
  await page.getByLabel('City / town / village').fill('Chennai');
  await page.getByRole('combobox', { name: 'State / UT' }).click();
  await page.getByRole('option', { name: 'Tamil Nadu', exact: true }).click();
  await page.getByLabel('PIN code').fill('600020');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'This record is not available here' })).toBeVisible();
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');

// #889: someone else completed the record first. The save is refused with 409 "changed" and the screen says so; Reload reads the record
// again (what was typed is dropped), and the next save carries the new version.
const fillAddress = async (page: Page) => {
  await page.locator('#line1').fill('4 Synthetic Street');
  await page.locator('#city').fill('Chennai');
  await page.locator('#region').click();
  await page.getByRole('option').first().click();
  await page.locator('#postalCode').fill('600020');
  await page.locator('form button[type=submit]').click();
};
for (const locale of ['en', 'ar'])
  test(`a save from a stale record says it was changed by someone else, reloads, and saves with the new version (${locale})`, async ({ page }) => {
    const saves = await open(page, { locale });
    let version = 7;
    await page.route(`**/v1/patients/${id}`, r => r.fulfill({ json: { ...leela, version } }));
    await page.route(`**/v1/patients/${id}/completion`, r => {
      saves.push(r.request());
      const sent = (r.request().postDataJSON() as { version: number }).version;
      return sent === version ? r.fulfill({ json: { status: 'Incomplete' } }) : r.fulfill({ status: 409, json: { code: 'changed' } });
    });
    await page.goto(`/patients/directory/${id}/complete`);
    await expect(page.locator('#line1')).toBeVisible();
    version = 8; // the other desk saved after this page read the record
    await fillAddress(page);

    const alert = page.getByRole('alert').filter({ has: page.getByRole('button') });
    await expect(alert).toBeVisible();
    if (locale === 'en') await expect(alert).toContainText('This record was changed by someone else. Nothing was saved.');
    else await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    expect((saves.at(-1)!.postDataJSON() as { version: number }).version).toBe(7);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
    expect(violations).toEqual([]);

    await alert.getByRole('button').click();
    await expect(alert).toHaveCount(0);
    await expect(page.locator('#line1')).toHaveValue('');
    await fillAddress(page);
    await expect(page.locator('form')).toHaveCount(0); // saved: the form gives way to the status line
    expect((saves.at(-1)!.postDataJSON() as { version: number }).version).toBe(8);
  });

for (const locale of ['en', 'hi', 'ar'])
  for (const width of [1366, 375])
    test(`record task and completion form are axe clean and fit (${locale}, ${width}px)`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await open(page, { locale });
      for (const path of [`/patients/directory/${id}`, `/patients/directory/${id}/complete`]) {
        await page.goto(path);
        // The loaded page, not its loading state, is what this test checks: the record's h1 is the patient's name only once loaded, and the form has inputs.
        // Not networkidle: the shell's unmocked calls (the notification list, the board hub) keep retrying here, so the network is never idle for long.
        await expect(path.endsWith('/complete') ? page.locator('main input').first() : page.getByRole('heading', { level: 1, name: leela.fullName })).toBeVisible();
        await expect(page.locator('main h1')).toBeVisible();
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
        // One pass in the page: a locator list measured one by one goes stale (and hangs) if the page re-renders in between.
        const small = await page.locator('main button, main a, main input').evaluateAll(els => els
          .filter(e => e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden' && e.getBoundingClientRect().height < 44)
          .map(e => `${e.tagName.toLowerCase()} "${(e.textContent || e.getAttribute('aria-label') || '').trim().slice(0, 30)}" ${Math.round(e.getBoundingClientRect().height)}px`));
        expect(small).toEqual([]);
        await page.addScriptTag({ path: axePath });
        const violations = await page.evaluate(async () =>
          (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
            .map(v => `${v.id} (${v.nodes.length})`));
        expect(violations).toEqual([]);
      }
    });

test('while loading and when refused, the record page still has its one h1 and is axe clean', async ({ page }) => {
  await page.route('**/v1/me/context', r => r.fulfill({ json: desk(full) }));
  let release!: () => void;
  const held = new Promise<void>(resolve => { release = resolve; });
  await page.route(`**/v1/patients/${id}`, async r => { await held; await r.fulfill({ status: 403, json: {} }); });
  await page.goto(`/patients/directory/${id}`);
  for (const text of ['Opening record…', 'You don’t have access to this facility.']) {
    await expect(page.locator('main h1')).toHaveText(text);
    await expect(page.locator('main h1')).toHaveCount(1);
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string }[] }> } }).axe.run()).violations.map(v => v.id));
    expect(violations).toEqual([]);
    release();
  }
});

// MDV-AUDIT-1101: the desk record's Book, Check in and Edit details; a phone is corrected alone and only what was typed is sent.
test('the record offers Book, Check in and Edit details; editing a phone sends only the phone', async ({ page }) => {
  const saves = await open(page);
  await expect(page.getByRole('link', { name: 'Book', exact: true })).toHaveAttribute('href', `/schedule?patient=${id}`);
  await expect(page.getByRole('link', { name: 'Check in', exact: true })).toHaveAttribute('href', `/front-desk/walk-in?patient=${id}`);
  await page.getByRole('link', { name: 'Edit details' }).click();
  await expect(page).toHaveURL(`/patients/directory/${id}/complete`);

  await page.getByLabel('Mobile', { exact: true }).fill('12');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Check the mobile number: every digit, with or without the country code.')).toBeVisible();
  expect(saves).toHaveLength(0);

  await page.getByLabel('Mobile', { exact: true }).fill('+91 90000 00152');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.locator('form')).toHaveCount(0);
  expect(saves[0]!.postDataJSON()).toEqual({ address: null, payer: null, emergencyContact: null, contact: { mobile: '+91 90000 00152', alternatePhone: null, email: null }, version: 7 });
});

test('a role that may only read patients is offered none of Book, Check in or Edit details', async ({ page }) => {
  await open(page, { capabilities: { RegisterBookPatients: 'Read' } });
  for (const name of ['Book', 'Check in', 'Edit details']) await expect(page.getByRole('link', { name, exact: true })).toHaveCount(0);
});
