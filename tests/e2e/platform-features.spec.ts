import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { t } from './support/desk';

// MDV-P5-041: the console's Features page against a mocked console API. The server rules (roles, reason, clinical sign-off, locked features, the narrow
// medvya_features login, 404 in the clinic when off) are tested on PostgreSQL in FeatureFlagTests.cs and PlatformFeatureTests.cs; here the browser path
// is real: the country switch, the stage form, the sign-off refusal shown on its field, turn off for all, read-only roles, Arabic.
test.use({ baseURL: 'http://localhost:4175' });

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: { html: string }[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length}) ${v.nodes.map(n => n.html.slice(0, 160)).join(' | ')}`));
}

const me = (role: string) => ({ displayName: 'Ops Admin (synthetic)', role, keyVerified: true, hasKey: true, canRegister: false });
const row = (key: string, over: object = {}) => ({
  key, clinical: false, clinicSwitch: false, alwaysOn: false, stage: 'Everyone', isNew: true, percentage: 0, organisations: [], clinicalSignOff: null, changedAt: null, ...over,
});
const country = (code: string, nurseStage: string) => ({
  country: code,
  features: [
    row('clinical.nurse-intake', { clinical: true, clinicSwitch: true, stage: nurseStage, isNew: nurseStage === 'Everyone', changedAt: nurseStage === 'Everyone' ? null : '2026-11-14T06:00:00Z' }),
    row('billing.invoices', { clinicSwitch: true }),
    row('patients.record', { clinical: true, alwaysOn: true, isNew: false }),
    row('prescribing.signing', { clinical: true, alwaysOn: true, isNew: false }),
  ],
});

async function mock(page: Page, role: string) {
  const api = { posts: [] as { path: string; body: Record<string, unknown> }[] };
  await page.route('**/v1/me', r => r.fulfill({ json: me(role) }));
  await page.route('**/v1/features', r => r.fulfill({ json: [country('AE', 'Everyone'), country('IN', 'Beta')] }));
  await page.route('**/v1/features/**', r => {
    const body = r.request().postDataJSON() as Record<string, unknown>;
    api.posts.push({ path: new URL(r.request().url()).pathname, body });
    // The server refuses a clinical feature at Everyone until a sign-off is recorded (FeatureReleaseConsole).
    if (body.stage === 'Everyone' && !body.clinicalSignOff)
      return r.fulfill({ status: 400, json: { errors: { clinicalSignOff: ['required'] } } });
    return r.fulfill({ status: 204 });
  });
  return api;
}

test('an operations member moves a clinical feature to everyone only with a sign-off, each change with a reason', async ({ page }) => {
  const api = await mock(page, 'Operations');
  await page.goto('/features');
  await expect(page.getByRole('heading', { name: 'Features' })).toBeVisible();
  // AE comes first from the server; India is one click away and keeps its own release.
  await page.getByRole('button', { name: 'India' }).click();
  const table = page.getByRole('region', { name: 'Features' });
  await expect(table.getByRole('row', { name: /Nurse intake/ })).toContainText('Beta');
  await expect(table.getByRole('row', { name: /Patient record/ })).toContainText('Always on');
  await expect(table.getByRole('button', { name: 'Change the release of Patient record' })).toHaveCount(0); // locked
  expect(await axe(page)).toEqual([]);

  await table.getByRole('button', { name: 'Change the release of Nurse intake' }).click();
  await expect(page.getByRole('heading', { name: 'Change the release of Nurse intake in IN' })).toBeVisible();
  await page.getByRole('combobox', { name: 'Release' }).click();
  await page.getByRole('option', { name: 'Everyone' }).click();
  const signOff = page.getByRole('textbox', { name: 'Clinical sign-off' });
  await expect(signOff).toBeVisible(); // asked because the feature is clinical and none is recorded
  await page.getByRole('button', { name: 'Save release' }).click();
  await expect(page.getByText('Give a reason of at least 5 characters.').first()).toBeVisible();
  expect(api.posts).toEqual([]); // nothing leaves the browser without a reason

  await page.getByRole('textbox', { name: 'Reason (saved in the audit log)' }).fill('Pilot clinics asked for nurse intake');
  await page.getByRole('button', { name: 'Save release' }).click();
  await expect(page.getByText('Record the clinical sign-off before this clinical feature reaches everyone.').first()).toBeVisible(); // the server's refusal, on its field
  expect(await axe(page)).toEqual([]);

  await signOff.fill('Clinical safety officer (synthetic), HL-041');
  await page.getByRole('button', { name: 'Save release' }).click();
  await expect(page.getByRole('heading', { name: 'Change the release of Nurse intake in IN' })).toHaveCount(0);
  expect(api.posts.at(-1)).toEqual({ path: '/v1/features/IN/clinical.nurse-intake', body: {
    stage: 'Everyone', percentage: null, organisations: [], clinicalSignOff: 'Clinical safety officer (synthetic), HL-041', reason: 'Pilot clinics asked for nurse intake' } });
});

test('turn off for all asks for a reason and confirms in the bottom bar', async ({ page }) => {
  const api = await mock(page, 'Owner');
  await page.goto('/features');
  await page.getByRole('button', { name: 'Turn off for all clinics: Billing' }).click();
  await expect(page.getByText('Every clinic in every country loses this feature on its next request')).toBeVisible();
  await page.getByRole('textbox', { name: 'Reason (saved in the audit log)' }).fill('Incident: payments fail for some clinics');
  expect(await axe(page)).toEqual([]);
  await page.getByRole('button', { name: 'Turn off for all', exact: true }).click();
  await expect(page.getByText('Every clinic in every country loses this feature on its next request')).toHaveCount(0);
  expect(api.posts).toEqual([{ path: '/v1/features/billing.invoices/off', body: { reason: 'Incident: payments fail for some clinics' } }]);
});

for (const role of ['Support', 'Finance', 'Reviewer']) {
  test(`${role} reads the releases but has no change buttons`, async ({ page }) => {
    const api = await mock(page, role);
    await page.goto('/features');
    await expect(page.getByRole('region', { name: 'Features' }).getByRole('row', { name: /Nurse intake/ })).toBeVisible();
    await expect(page.getByRole('button', { name: /Change the release of|Turn off for all/ })).toHaveCount(0);
    expect(api.posts).toEqual([]);
  });
}

test('the Features page reads in Arabic, right to left, axe clean, and stacks on a phone', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'ar'));
  await mock(page, 'Owner');
  await page.goto('/features');
  await expect(page.getByRole('heading', { name: t('ar', 'platform.features.title') })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.dir)).toBe('rtl');
  await expect(page.getByRole('link', { name: t('ar', 'platform.nav.features') })).toBeVisible();
  expect(await axe(page)).toEqual([]);
  await page.getByRole('button', { name: t('ar', 'platform.features.changeFor', { feature: t('ar', 'platform.features.name.billing.invoices') }) }).click();
  await expect(page.getByRole('button', { name: t('ar', 'platform.features.save') })).toBeVisible();
  expect(await axe(page)).toEqual([]);

  await page.setViewportSize({ width: 375, height: 740 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
