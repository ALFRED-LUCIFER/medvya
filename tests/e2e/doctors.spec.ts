import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-007 against a mocked API (CI has none; the real API is covered by DoctorTests and the browser check in the PR).
// Synthetic cast only (docs/00-overview/personas.md).
const admin = {
  user: { id: 'u-lakshmi', displayName: 'K. Lakshmi (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic (synthetic)', facilityId: 'f-1', facilityName: 'Adyar (synthetic)', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', country: 'IN', role: 'PracticeAdmin', flags: [], capabilities: { StaffRolesIntegrations: 'Config' } },
};
const existing = {
  id: 'd-1', fullName: 'Dr. Arjun Rao (synthetic)', specialty: 'Family medicine', qualifications: null, languages: null, facilityId: 'f-1', facilityName: 'Adyar (synthetic)',
  status: 'Active', authority: 'TNMC', number: '84213', category: null, expiresOn: null, expired: true, attestedBy: 'u-lakshmi', attestedByName: null, attestedAt: '2026-11-01T09:00:00Z', hasSignature: false,
};
const list = (doctors: object[]) => ({
  doctors, defaults: { newVisitFee: 50000, followUpFee: 30000, currency: 'INR' },
  authorities: [{ code: 'TNMC', name: 'Tamil Nadu Medical Council' }, { code: 'NMC', name: 'National Medical Commission' }],
});

async function setUp(page: Page, locale = 'en') {
  const state = { doctors: [existing] as object[], added: null as unknown, resubmitted: null as unknown, signature: null as string | null };
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: admin }));
  await page.route('**/v1/settings/locations', r => r.fulfill({ json: [{ id: 'f-1', name: 'Adyar (synthetic)', address: null, timeZone: 'Asia/Kolkata' }] }));
  await page.route('**/v1/settings/doctors', async r => {
    if (r.request().method() === 'POST') {
      state.added = r.request().postDataJSON();
      const body = state.added as { fullName: string };
      const doctor = { ...existing, id: 'd-2', fullName: body.fullName, status: 'Invited', expired: false, attestedAt: '2026-11-10T09:00:00Z' };
      state.doctors = [...state.doctors, doctor];
      return r.fulfill({ status: 201, json: doctor });
    }
    return r.fulfill({ json: list(state.doctors) });
  });
  await page.route('**/v1/settings/doctors/*/licence', async r => {
    state.resubmitted = r.request().postDataJSON();
    state.doctors = [{ ...existing, expired: false, attestedAt: '2026-11-10T09:00:00Z' }];
    return r.fulfill({ json: state.doctors[0] });
  });
  await page.route('**/v1/settings/doctors/*/signature', async r => {
    state.signature = r.request().headers()['content-type'] ?? null;
    return r.fulfill({ status: 204 });
  });
  return state;
}

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
}

test('the Doctors tab lists doctors with who attested the licence, and an expired licence warns and is renewed with a new attestation', async ({ page }) => {
  const state = await setUp(page);
  await page.goto('/settings?tab=doctors');

  await expect(page.getByRole('tab', { name: 'Doctors' })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('heading', { name: 'Dr. Arjun Rao (synthetic)' })).toBeVisible();
  await expect(page.getByText('Licence expired')).toBeVisible();
  await expect(page.getByText('This licence has passed its expiry date. Renew it and confirm it again. The doctor is not blocked from working.')).toBeVisible();
  await expect(page.getByText('Signing in Medvya is always an authenticated action by the doctor (passkey / MFA), never the image alone.')).toBeVisible();

  await page.getByRole('button', { name: 'Renew licence' }).click();
  await page.getByLabel('Registration category').fill('Permanent');
  await page.getByRole('button', { name: 'Save and confirm' }).click();
  await expect(page.getByLabel('Expiry date')).toHaveAttribute('aria-invalid', 'true'); // expiry is required
  await page.getByLabel('Expiry date').fill('2031-12-31');
  await page.getByRole('button', { name: 'Save and confirm' }).click();
  await expect(page.getByLabel(/I confirm this licence belongs to this doctor/)).toHaveAttribute('aria-invalid', 'true'); // the owner must attest
  await expect(page.getByText('Your organisation answers for this licence. Medvya does not check it.')).toBeVisible(); // MDV-P1-067 reminder
  await page.getByLabel(/I confirm this licence belongs to this doctor/).check();
  await page.getByRole('button', { name: 'Save and confirm' }).click();
  await expect(page.getByText('Attested by the owner on 10 Nov 2026')).toBeVisible();
  expect(state.resubmitted).toMatchObject({ authority: 'TNMC', number: '84213', category: 'Permanent', expiresOn: '2031-12-31', attested: true });
});

test('Add doctor: six steps, fees prefilled in whole currency units, then one request and the signature image', async ({ page }) => {
  const state = await setUp(page);
  await page.goto('/settings?tab=doctors');
  await page.getByRole('button', { name: 'Add doctor' }).first().click();

  await expect(page.getByText('Step 1 of 6: Details')).toBeVisible();
  await page.getByRole('button', { name: 'Next' }).click(); // nothing typed: refused, still on step 1
  await expect(page.getByText('Step 1 of 6: Details')).toBeVisible();
  await expect(page.getByLabel('Full name')).toHaveAttribute('aria-invalid', 'true');

  await page.getByLabel('Full name').fill('Dr. Sana Khan (synthetic)');
  await page.getByLabel('Email for the invitation').fill('sana.khan@example.test');
  await page.getByLabel('Specialty').fill('Paediatrics');
  await page.getByRole('button', { name: 'Next' }).click();

  await expect(page.getByText('Step 2 of 6: Licence')).toBeVisible();
  await page.getByRole('combobox', { name: 'Medical council' }).click();
  await page.getByRole('option', { name: 'Tamil Nadu Medical Council' }).click();
  await page.getByLabel('Registration number').fill('84213');
  await page.getByLabel('Registration category').fill('Permanent');
  await page.getByLabel('Expiry date').fill('2031-12-31');
  await page.getByRole('button', { name: 'Next' }).click();
  await expect(page.getByText('Step 2 of 6: Licence')).toBeVisible(); // not without the owner's attestation
  await page.getByLabel(/I confirm this licence belongs to this doctor/).check();
  await page.getByRole('button', { name: 'Next' }).click();

  await expect(page.getByText('Step 3 of 6: Schedule')).toBeVisible();
  await page.getByRole('button', { name: 'Next' }).click();

  await expect(page.getByText('Step 4 of 6: Default fees')).toBeVisible();
  await expect(page.getByLabel('New patient fee (INR)')).toHaveValue('500');
  await expect(page.getByLabel('Follow-up fee (INR)')).toHaveValue('300');
  await page.getByRole('button', { name: 'Next' }).click();
  await expect(page.getByText('Step 5 of 6: Access')).toBeVisible();
  await expect(page.getByLabel('Role')).toHaveValue('Doctor');
  await page.getByRole('button', { name: 'Next' }).click();

  await expect(page.getByText('Step 6 of 6: Signature image')).toBeVisible();
  await expect(page.getByText('Signing in Medvya is always an authenticated action by the doctor (passkey / MFA), never the image alone.')).toBeVisible();
  expect(await axe(page)).toEqual([]);
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64');
  await page.getByLabel('Signature image (optional)').setInputFiles({ name: 'signature.png', mimeType: 'image/png', buffer: png });
  await page.getByRole('button', { name: 'Add doctor and send invitation' }).click();

  await expect(page.getByText('Dr. Sana Khan (synthetic) was added and the invitation was sent.')).toBeVisible();
  expect(state.added).toMatchObject({
    fullName: 'Dr. Sana Khan (synthetic)', email: 'sana.khan@example.test', authority: 'TNMC', licenceNumber: '84213', category: 'Permanent', expiresOn: '2031-12-31', attested: true,
    newVisitFee: 50000, followUpFee: 30000, facilityId: 'f-1', qualifications: null,
  });
  expect(state.signature).toBe('image/png');
});

for (const locale of ['en', 'ar'])
  test(`the Doctors tab is accessible at tablet width (${locale})`, async ({ page }) => {
    await setUp(page, locale);
    await page.setViewportSize({ width: 768, height: 1024 });
    await page.goto('/settings?tab=doctors');
    await expect(page.getByRole('heading', { level: 3 })).toHaveText('Dr. Arjun Rao (synthetic)');
    expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === 'ar' ? 'rtl' : 'ltr');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
    expect(await axe(page)).toEqual([]);
  });
