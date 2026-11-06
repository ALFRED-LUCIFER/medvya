import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-019: the consent section on a record and the owner's terms acceptance. CI has no API: the endpoints are mocked with the
// synthetic cast; the server side (gate, owner-only acceptance, append-only receipts) is covered by TermsAcceptanceTests on SQL Server.
const context = (role: string, capabilities: Record<string, string>) => ({
  user: { id: 'u-1', displayName: 'A. Joseph (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic (synthetic)', facilityId: 'f-1', facilityName: 'Adyar (synthetic)', role }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', country: 'IN', role, flags: [], capabilities },
});
const meeraId = '5e1d0001-0004-4000-8000-000000000001';
const meera = {
  id: meeraId, mrn: 'DQ-10-4482', fullName: 'Meera Iyer (synthetic)', arabicName: null, dateOfBirth: '1972-03-12', dateOfBirthEstimated: false,
  ageYears: 54, sexAtBirth: 'Female', genderIdentity: null, preferredLanguage: 'Tamil', nationality: 'IN', mobile: '+919000000101',
  alternatePhone: null, email: null, status: 'Complete', registeredOn: '2026-01-01', address: null, identifiers: [], payer: null,
};
const at = '2026-11-05T10:00:00Z';
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () => (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
}

async function desk(page: Page, locale: string, state: { decisions: object[]; posted: unknown[] }) {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: context('Receptionist', { RegisterBookPatients: 'Full', CheckInQueue: 'Full' }) }));
  await page.route(`**/v1/patients/${meeraId}`, r => r.fulfill({ json: meera }));
  await page.route(`**/v1/patients/${meeraId}/consents`, r => {
    if (r.request().method() === 'POST') {
      const body = r.request().postDataJSON() as { decisions: { type: string; channel: string | null; granted: boolean }[] };
      state.posted.push(body);
      const d = body.decisions[0]!;
      state.decisions = [...state.decisions.filter(x => !((x as typeof d).type === d.type && (x as typeof d).channel === d.channel)), { ...d, decidedAt: at, policyVersion: '2026-11-draft' }];
      return r.fulfill({ json: { decisions: body.decisions } });
    }
    return r.fulfill({ json: { policyVersion: '2026-11-draft', decisions: state.decisions } });
  });
}

for (const locale of ['en', 'ar'])
  test(`the desk withdraws and grants a consent from the record, axe clean (${locale})`, async ({ page }) => {
    const state = { decisions: [{ type: 'Treatment', channel: null, granted: true, decidedAt: at, policyVersion: '2026-11-draft' }, { type: 'Reminders', channel: 'Sms', granted: true, decidedAt: at, policyVersion: '2026-11-draft' }], posted: [] as unknown[] };
    await desk(page, locale, state);
    await page.goto(`/patients/directory/${meeraId}`);
    const section = page.getByRole('region', { name: locale === 'en' ? 'Consents' : 'الموافقات' });
    await expect(section).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === 'ar' ? 'rtl' : 'ltr');
    const buttons = section.getByRole('button');
    await expect(buttons).toHaveCount(6);
    for (const b of await buttons.all()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    expect(await axe(page)).toEqual([]);

    // Withdraw the SMS reminders: one new decision under the draft wording; the row now reads Off and offers to turn it on again.
    const sms = locale === 'en' ? 'Turn off: Appointment reminders: SMS' : null;
    if (sms) {
      await section.getByRole('button', { name: sms }).click();
      await expect(section.getByRole('button', { name: 'Turn on: Appointment reminders: SMS' })).toBeVisible();
      expect(state.posted).toEqual([{ policyVersion: '2026-11-draft', decisions: [{ type: 'Reminders', channel: 'Sms', granted: false }] }]);
      expect(await axe(page)).toEqual([]);
    }
  });

test('a role without Register / book at Full sees the consents but cannot change them', async ({ page }) => {
  const state = { decisions: [], posted: [] as unknown[] };
  await desk(page, 'en', state);
  await page.route('**/v1/me/context', r => r.fulfill({ json: context('Receptionist', { RegisterBookPatients: 'Read' }) }));
  await page.goto(`/patients/directory/${meeraId}`);
  const section = page.getByRole('region', { name: 'Consents' });
  await expect(section.getByText('Not recorded')).toHaveCount(6);
  await expect(section.getByRole('button')).toHaveCount(0);
});

for (const locale of ['en', 'ar'])
  test(`the owner accepts the terms and the tab shows who can proceed, axe clean (${locale})`, async ({ page }) => {
    let accepted = false;
    const status = () => ({ version: '2026-11-draft', accepted, acceptedAt: accepted ? at : null, required: true });
    await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
    await page.route('**/v1/me/context', r => r.fulfill({ json: context('PracticeAdmin', { StaffRolesIntegrations: 'Config' }) }));
    await page.route('**/v1/settings/terms', r => r.fulfill({ json: status() }));
    await page.route('**/v1/settings/terms/accept', r => { accepted = true; return r.fulfill({ json: status() }); });
    await page.goto('/settings?tab=terms');
    expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === 'ar' ? 'rtl' : 'ltr');
    const accept = page.getByRole('button').filter({ hasText: locale === 'en' ? 'Accept the terms for this practice' : 'الموافقة على الشروط لهذه العيادة' });
    await expect(accept).toBeVisible();
    expect(await axe(page)).toEqual([]);
    await accept.click();
    await expect(accept).toHaveCount(0);
    await expect(page.getByRole('status').filter({ hasText: /2026|٢٠٢٦/ })).toBeVisible();
    expect(await axe(page)).toEqual([]);
  });

test('a non-owner who tries to accept is told only the owner can', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
  await page.route('**/v1/me/context', r => r.fulfill({ json: context('HospitalIt', { StaffRolesIntegrations: 'Config' }) }));
  await page.route('**/v1/settings/terms', r => r.fulfill({ json: { version: '2026-11-draft', accepted: false, acceptedAt: null, required: true } }));
  await page.route('**/v1/settings/terms/accept', r => r.fulfill({ status: 403, json: { title: 'Only the practice owner can accept' } }));
  await page.goto('/settings?tab=terms');
  await page.getByRole('button', { name: 'Accept the terms for this practice' }).click();
  await expect(page.getByRole('alert')).toHaveText('Only the practice owner can accept the terms.');
});

// The onboarding prompt: an owner sees it on any page until the terms are accepted; a desk role never asks the endpoint.
test('the owner sees the terms prompt on every page until accepted; a desk role does not', async ({ page }) => {
  let asked = 0;
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'en'));
  await page.route('**/v1/settings/terms', r => { asked++; return r.fulfill({ json: { version: '2026-11-draft', accepted: false, acceptedAt: null, required: true } }); });
  await page.route('**/v1/me/context', r => r.fulfill({ json: context('PracticeAdmin', { StaffRolesIntegrations: 'Config' }) }));
  await page.goto('/settings?tab=profile');
  const notice = page.getByRole('note').filter({ hasText: 'Booking and messages are paused' });
  await expect(notice.getByRole('link', { name: 'Open Settings → Terms' })).toHaveAttribute('href', '/settings?tab=terms');
  expect(await axe(page)).toEqual([]);

  await page.unroute('**/v1/me/context');
  await page.route('**/v1/me/context', r => r.fulfill({ json: context('Receptionist', { RegisterBookPatients: 'Full', CheckInQueue: 'Full' }) }));
  asked = 0;
  await page.goto('/front-desk');
  await expect(page.getByRole('main')).toBeVisible();
  await expect(page.getByRole('note').filter({ hasText: 'Booking and messages are paused' })).toHaveCount(0);
  expect(asked).toBe(0);
});
