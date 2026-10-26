import { createRequire } from 'node:module';
import { expect, test, type Page, type Request } from '@playwright/test';

// MDV-P1-013 (India) and MDV-P1-014 (UAE) full registration in steps (P-02). CI has no API: /v1/me/context and /v1/patients/*
// are mocked with the synthetic cast; the server rules are covered by PatientRulesTests and PatientRegistrationFlowTests.
const context = (country: 'IN' | 'AE') => ({
  user: { id: 'u-joseph', displayName: 'A. Joseph' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Synthetic Clinic', facilityId: 'f-1', facilityName: 'Synthetic Clinic · Front desk', role: 'Receptionist' }],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Receptionist', flags: [], country,
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', InvoicesPayments: 'Full', Patient360Chart: 'None', ClinicalNotes: 'None' },
  },
});

async function desk(page: Page, country: 'IN' | 'AE', { locale = 'en', warnings = [] as string[] } = {}) {
  const saves: Request[] = [];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: context(country) }));
  await page.route('**/v1/patients/duplicate-check', r => r.fulfill({ json: { candidates: [] } }));
  await page.route('**/v1/patients', r => {
    saves.push(r.request());
    return r.fulfill({ json: { patientId: '5e1d0001-0004-4000-8000-000000000411', mrn: '001094', registeredAt: '2026-10-25T04:46:00Z', status: 'Complete', warnings } });
  });
  await page.goto('/patients/new');
  return saves;
}

async function choose(page: Page, label: string, option: string) {
  await page.getByRole('combobox', { name: label, exact: true }).click();
  await page.getByRole('option', { name: option, exact: true }).click();
}

const next = (page: Page) => page.getByRole('button', { name: 'Continue' }).click();

test('India: full registration in steps, with Aadhaar last 4 only, the PIN rule and the payer, saved once from Review', async ({ page }) => {
  const saves = await desk(page, 'IN');
  await expect(page.getByRole('link', { name: 'Full registration' })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByText('Verify with ABHA · coming later.')).toBeVisible();

  await page.getByLabel('Full name').fill('Lakshmi Pillai');
  await page.getByRole('textbox', { name: 'Date of birth', exact: true }).fill('1984-05-09');
  await page.getByRole('radio', { name: 'Female', exact: true }).check();
  await page.getByLabel('ABHA number (optional)').fill('91-0000-0000-0411');
  await choose(page, 'Other ID type', 'Aadhaar (last 4 only)');
  await page.getByLabel('Last 4 digits of Aadhaar').fill('0000 1111 2222');
  await next(page);
  await expect(page.getByText('Enter only the last 4 digits. The full Aadhaar number is never stored.')).toBeVisible();
  await page.getByLabel('Last 4 digits of Aadhaar').fill('0411');
  await next(page);

  // Each step starts at its heading, so keyboard and screen-reader users begin at the top.
  await expect(page.getByRole('heading', { name: 'Contact & address', level: 2 })).toBeFocused();
  await page.getByLabel('Mobile').fill('+91 90000 00411');
  await page.getByLabel('House / flat, building').fill('12/402 Synthetic Apartments');
  await page.getByLabel('City / town / village').fill('Kochi');
  await choose(page, 'State / UT', 'Kerala');
  await page.getByLabel('PIN code').fill('68200');
  await next(page);
  await expect(page.getByText('PIN code must be 6 digits')).toBeVisible();
  await page.getByLabel('PIN code').fill('682001');
  await next(page);

  // Family & emergency (MDV-P1-020): the emergency contact is required for full registration; an adult needs no guardian.
  await expect(page.getByText('Not required · adult.')).toBeVisible();
  await next(page);
  await expect(page.getByText('Required.')).toBeVisible();
  await page.getByLabel('Emergency contact name').fill('Suresh Pillai');
  await choose(page, 'Relationship', 'Spouse');
  await page.getByLabel('Emergency contact phone').fill('+91 90000 00412');
  await expect(page.getByText('Emergency contacts are not given portal access.', { exact: false })).toBeVisible();
  await next(page);

  await choose(page, 'Payment type', 'Insurance');
  await page.getByLabel('Insurer').fill('Synthetic Health');
  await page.getByLabel('TPA (optional)').fill('Synthetic TPA');
  await page.getByLabel('Policy / member number').fill('SYN-23-000411-01');
  await next(page);

  await expect(page.getByRole('heading', { name: 'Review & duplicate check' })).toBeFocused();
  await expect(page.getByText('Aadhaar (last 4 digits) 0411')).toBeVisible();
  await expect(page.getByText('Insurance · Synthetic Health · Synthetic TPA · SYN-23-000411-01')).toBeVisible();
  expect(saves).toHaveLength(0);
  await page.getByRole('button', { name: 'Register patient' }).click();

  await expect(page.getByRole('heading', { name: 'Lakshmi Pillai is registered' })).toBeVisible();
  await expect(page.getByText(/MRN 001094 · registered .* by A\. Joseph/)).toBeVisible();
  for (const action of ['Book appointment', 'Check in now (walk-in)']) await expect(page.getByRole('link', { name: action })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Register another' })).toBeVisible();
  expect(saves).toHaveLength(1);
  const body = saves[0]!.postDataJSON();
  expect(new URL(saves[0]!.url()).search).toBe('');
  expect(body).toMatchObject({
    quick: false, fullName: 'Lakshmi Pillai', dateOfBirth: '1984-05-09', sexAtBirth: 'Female', mobile: '+91 90000 00411',
    identifiers: [{ scheme: 'Abha', value: '91-0000-0000-0411' }, { scheme: 'AadhaarLast4', value: '0411' }],
    address: { line1: '12/402 Synthetic Apartments', city: 'Kochi', region: 'Kerala', postalCode: '682001', makani: null },
    payer: { kind: 'PrivateInsurance', payerName: 'Synthetic Health', network: 'Synthetic TPA', memberNumber: 'SYN-23-000411-01' },
    emergencyContact: { name: 'Suresh Pillai', relationship: 'Spouse', phone: '+91 90000 00412' }, guardian: null,
  });
  expect(body).not.toHaveProperty('countryCode'); // the country is the tenant's, never sent
  expect(body).not.toHaveProperty('facilityId');
});

test('UAE: Arabic name typed right to left, an expired Emirates ID warns, the emirate names its HIE', async ({ page }) => {
  const saves = await desk(page, 'AE', { warnings: ['emirates_id.expired'] });
  await page.getByLabel('Name as on Emirates ID').fill('Fatima Al Mansoori');
  const arabic = page.getByLabel('Full name (Arabic)');
  await expect(arabic).toHaveAttribute('dir', 'rtl');
  await arabic.fill('فاطمة المنصوري');
  await expect(page.locator('aside p[dir="rtl"]')).toHaveText('فاطمة المنصوري'); // preview card
  await page.getByRole('textbox', { name: 'Date of birth', exact: true }).fill('1988-02-03');
  await page.getByRole('radio', { name: 'Female', exact: true }).check();
  await next(page);
  await expect(page.getByText('Enter the Emirates ID, or a passport for visitors.')).toBeVisible();
  await page.getByLabel('Emirates ID', { exact: true }).fill('784-1988-123');
  await next(page);
  await expect(page.getByText('Format 784-YYYY-NNNNNNN-N').last()).toBeVisible();
  await page.getByLabel('Emirates ID', { exact: true }).fill('784-1988-1234567-1');
  await page.getByLabel('Emirates ID expiry').fill('2020-11-30');
  await expect(page.getByText('This Emirates ID has expired. You can still register the patient; ask them to renew it.')).toBeVisible();
  await next(page); // a warning never blocks

  await page.getByLabel('Mobile').fill('050 000 0458');
  await page.getByLabel('Villa / building & flat').fill('Villa 14');
  await choose(page, 'Emirate', 'Dubai');
  await expect(page.getByText('Health information exchange for this emirate: NABIDH. No connection is made at registration.')).toBeVisible();
  await next(page);
  await page.getByLabel('Emergency contact name').fill('Ahmed Al Mansoori');
  await choose(page, 'Relationship', 'Spouse');
  await page.getByLabel('Emergency contact phone').fill('050 000 0459');
  await next(page);
  await next(page); // payer left for later
  await expect(page.getByText('فاطمة المنصوري', { exact: false }).first()).toBeVisible();
  await page.getByRole('button', { name: 'Register patient' }).click();

  await expect(page.getByText('The Emirates ID has expired. The patient is registered; ask them to renew it.')).toBeVisible();
  expect(saves[0]!.postDataJSON()).toMatchObject({
    quick: false, arabicName: 'فاطمة المنصوري', payer: null,
    identifiers: [{ scheme: 'EmiratesId', value: '784-1988-1234567-1', expiresOn: '2020-11-30' }],
    address: { line1: 'Villa 14', region: 'DXB', city: null, postalCode: null },
  });
});

test('switching to quick mode keeps what was typed; only steps already reached can be opened', async ({ page }) => {
  await desk(page, 'IN');
  await expect(page.getByRole('button', { name: 'Payer' })).toBeDisabled();
  await page.getByLabel('Full name').fill('Kavya Iyer');
  await page.getByRole('link', { name: 'Quick (busy desk)' }).click();
  await expect(page).toHaveURL(/\?mode=quick$/);
  await expect(page.getByLabel('Full name')).toHaveValue('Kavya Iyer');
  await expect(page.getByRole('list', { name: 'Registration steps' })).toBeHidden();
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const [country, locale] of [['IN', 'en'], ['IN', 'hi'], ['AE', 'ar'], ['AE', 'en']] as const)
  for (const width of [1366, 768, 375])
    test(`full registration is axe clean, fits and keeps 44px targets (${country}, ${locale}, ${width}px)`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await desk(page, country, { locale });
      await page.locator('#fullName').fill(country === 'IN' ? 'Deepak Nair' : 'Fatima Al Mansoori');
      await page.locator('#dateOfBirth').fill('1979-08-14');
      await page.getByRole('radio').nth(1).check();
      if (country === 'AE') await page.locator('#emiratesId').fill('784-1988-1234567-1');
      await page.locator('form button[type=submit]').click();
      await expect(page.locator('#mobile')).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === 'ar' ? 'rtl' : 'ltr');
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      for (const b of await page.locator('main button, main a, main input:not([type=radio]):not([type=checkbox])').all())
        if (await b.isVisible()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
          .map(v => `${v.id} (${v.nodes.length})`));
      expect(violations).toEqual([]);
    });

test('a minor needs a guardian before saving, in quick mode too, and the guardian is sent', async ({ page }) => {
  const saves = await desk(page, 'IN');
  await page.getByRole('link', { name: 'Quick (busy desk)' }).click();
  // Type only once quick mode is on screen: a field filled while the full form is being replaced can miss the new input.
  await expect(page.getByRole('link', { name: 'Quick (busy desk)' })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByRole('heading', { name: 'Identity', exact: true, level: 2 })).toBeVisible();
  await page.getByLabel('Full name').fill('Arjun Nair');
  await page.getByLabel('Mobile').fill('+91 90000 00413');
  await page.getByRole('textbox', { name: 'Date of birth', exact: true }).fill('2016-05-01');
  await page.getByRole('radio', { name: 'Male', exact: true }).check();
  await expect(page.getByText('Required: the patient is under 18.')).toBeVisible();
  await page.getByRole('button', { name: 'Register patient' }).click();
  await expect(page.getByText('A guardian is required: the patient is under 18.')).toBeVisible();
  expect(saves).toHaveLength(0);

  await page.getByLabel('Guardian (if under 18)').fill('Ravi Nair');
  await choose(page, "Guardian's relationship", 'Parent');
  await page.getByLabel("Guardian's phone").fill('+91 90000 00414');
  await page.getByRole('button', { name: 'Register patient' }).click();
  await expect(page.getByRole('heading', { name: 'Arjun Nair is registered' })).toBeVisible();
  expect(saves[0]!.postDataJSON()).toMatchObject({ quick: true, guardian: { name: 'Ravi Nair', relationship: 'Parent', phone: '+91 90000 00414' }, emergencyContact: null });
});
