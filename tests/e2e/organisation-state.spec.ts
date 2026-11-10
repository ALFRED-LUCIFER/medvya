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

test('an approved organisation shows no notice', async ({ page }) => {
  await page.route('**/v1/me/context', r => r.fulfill({ json: context('Approved', { StaffRolesIntegrations: 'Config' }) }));
  await page.route('**/v1/settings/**', r => r.fulfill({ json: {} }));
  await page.goto('/settings');
  await expect(page.getByRole('navigation').first()).toBeVisible();
  await expect(page.getByRole('note').filter({ hasText: /Medvya has not approved|is suspended/ })).toHaveCount(0);
});
