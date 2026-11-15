import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-063 against a mocked API (the 403s and the state changes are in OrganisationStateTests on real PostgreSQL). J1 step 1: until Medvya
// approves the organisation the shell says so in words and an icon, and only set-up is open. Synthetic cast only.
const context = (organisationState: string, capabilities: Record<string, string>) => ({
  user: { id: 'u-lakshmi', displayName: 'K. Lakshmi (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic (synthetic)', facilityId: 'f-1', facilityName: 'Adyar (synthetic)', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', country: 'IN', role: 'PracticeAdmin', flags: [], organisationState, capabilities },
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
}

const notices = {
  en: { Draft: /Your organisation is being set up and Medvya has not approved it yet/, Suspended: /This organisation is suspended/ },
  ar: { Draft: /مؤسستك قيد الإعداد ولم توافق عليها Medvya بعد/, Suspended: /هذه المؤسسة معلّقة/ },
} as const;

for (const locale of ['en', 'ar'] as const)
  for (const state of ['Draft', 'Suspended'] as const)
    test(`the shell shows a plain notice while the organisation is ${state} and is accessible (${locale})`, async ({ page }) => {
      await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
      await page.route('**/v1/me/context', r => r.fulfill({ json: context(state, { StaffRolesIntegrations: 'Config' }) }));
      await page.route('**/v1/settings/**', r => r.fulfill({ json: {} }));
      await page.goto('/settings');
      await expect(page.getByRole('note').filter({ hasText: notices[locale][state] })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === 'ar' ? 'rtl' : 'ltr');
      expect(await axe(page)).toEqual([]);
    });

// #1365 F-026: while the organisation waits for approval, a closed screen says that, not "your role does not include this screen", and the
// owner gets the way to the request. A role without that way gets the start page instead.
for (const locale of ['en', 'ar'] as const)
  test(`a screen closed until approval says so, never that the role lacks it (${locale})`, async ({ page }) => {
    await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
    await page.route('**/v1/me/context', r => r.fulfill({ json: context('Submitted', { StaffRolesIntegrations: 'Config' }) }));
    await page.route('**/v1/settings/**', r => r.fulfill({ json: {} }));
    for (const path of ['/schedule', '/front-desk']) {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 })).toHaveText(locale === 'en' ? 'This screen is not open yet' : 'هذه الشاشة غير متاحة بعد');
      await expect(page.getByRole('main').locator('[data-slot=page-message]')).toContainText(locale === 'en' ? 'Waiting for approval. Medvya is reviewing your organisation.' : 'بانتظار الموافقة');
      await expect(page.getByRole('main')).not.toContainText(locale === 'en' ? 'Your role does not include' : 'دورك لا يشمل');
      await expect(page.getByRole('main').locator('[data-slot=page-message]').getByRole('link')).toHaveAttribute('href', '/organisation');
    }
    expect(await axe(page)).toEqual([]);
  });

test('an approved organisation still says a screen is not in the role', async ({ page }) => {
  await page.route('**/v1/me/context', r => r.fulfill({ json: context('Approved', { StaffRolesIntegrations: 'Config' }) }));
  await page.route('**/v1/settings/**', r => r.fulfill({ json: {} }));
  await page.goto('/schedule');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Screen not available');
});

test('an approved organisation shows no notice', async ({ page }) => {
  await page.route('**/v1/me/context', r => r.fulfill({ json: context('Approved', { StaffRolesIntegrations: 'Config' }) }));
  await page.route('**/v1/settings/**', r => r.fulfill({ json: {} }));
  await page.goto('/settings');
  await expect(page.getByRole('navigation').first()).toBeVisible();
  await expect(page.getByRole('note').filter({ hasText: /Medvya has not approved|is suspended/ })).toHaveCount(0);
});

// MDV-P2-055 AC4: an approved organisation (one approved before ADR-016 has no request: the endpoint answers 404) opens /organisation to
// "Approved" and the page asks nothing that answers 404; it has no menu entry, and the shell notice is gone once approved.
for (const [locale, approved] of [['en', 'Approved. Patients, booking and notes are on.'], ['ar', /^تمت الموافقة/]] as const)
  test(`an approved organisation's /organisation says Approved and no request answers 404, axe clean (${locale})`, async ({ page }) => {
    const notFound: string[] = [];
    page.on('response', r => { if (r.status() === 404) notFound.push(new URL(r.url()).pathname); });
    await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
    await page.route('**/v1/me/context', r => r.fulfill({ json: context('Approved', { StaffRolesIntegrations: 'Config' }) }));
    await page.route('**/v1/settings/**', r => r.fulfill({ json: {} }));
    await page.route('**/v1/organisation/request', r => r.fulfill({ status: 404, json: { title: 'Not found' } }));
    await page.goto('/organisation');
    await expect(page.getByRole('status').filter({ hasText: approved })).toBeVisible();
    await expect(page.getByRole('navigation').getByRole('link', { name: locale === 'en' ? /Organisation|request/i : /المؤسسة/ })).toHaveCount(0);
    await page.waitForLoadState('networkidle');
    expect(notFound.filter(p => p.startsWith('/v1/'))).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === 'ar' ? 'rtl' : 'ltr');
    expect(await axe(page)).toEqual([]);
  });
