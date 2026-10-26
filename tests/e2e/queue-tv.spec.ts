import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { fakeHub } from './support/fakeHub';

// MDV-P1-031: the waiting-room TV (P-05) and the staff "Queue displays" screen. CI has no API, so the endpoints are mocked in the
// shapes the server sends (IQueueService.cs). The TV must make no request but its own: pairing, its queue and its hub.
const token = `${'A'.repeat(40)}Synthetic_Display-Token1`; // 64 base64url characters, like a real display token
const queue = (now: { token: string; room: string | null; doctor: string }[]) => ({
  now, next: [{ token: 'A-08', waitMinutes: 10 }, { token: 'B-03', waitMinutes: 15 }, { token: 'A-09', waitMinutes: 25 }],
});
const called = [{ token: 'A-07', room: 'Room 3', doctor: 'Dr. Arjun Rao (synthetic)' }];

async function openTv(page: Page, { locale = 'en', paired = true, link = true } = {}) {
  const calls = { pairs: [] as unknown[], reads: 0, requests: [] as string[] };
  const state = { tv: queue(called) };
  page.on('request', r => { if (new URL(r.url()).pathname.startsWith('/v1/')) calls.requests.push(new URL(r.url()).pathname); });
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  // Counts chimes: an oscillator per tone.
  await page.addInitScript(() => {
    (window as unknown as { tones: number }).tones = 0;
    const Real = window.AudioContext;
    window.AudioContext = class extends Real {
      createOscillator() { (window as unknown as { tones: number }).tones++; return super.createOscillator(); }
    };
  });
  const hub = await fakeHub(page, '/v1/queue/hub', new RegExp(token)); // the display token never travels in a URL
  await page.route('**/v1/queue/tv/session', r => {
    calls.pairs.push(r.request().postDataJSON());
    return paired ? r.fulfill({ status: 204, body: '' }) : r.fulfill({ status: 401, json: { title: 'Display not recognised' } });
  });
  await page.route('**/v1/queue/tv', r => {
    calls.reads++;
    return paired ? r.fulfill({ json: state.tv }) : r.fulfill({ status: 401, json: {} });
  });
  await page.goto(link ? `/queue/tv#${token}` : '/queue/tv');
  return { calls, hub, state };
}

const tones = (page: Page) => page.evaluate(() => (window as unknown as { tones: number }).tones);

test('pairs from its link, then shows tokens, rooms, doctors and waits only', async ({ page }) => {
  const { calls } = await openTv(page);
  await expect(page.getByRole('heading', { name: 'Now calling' })).toBeVisible();
  const now = page.getByRole('region', { name: 'Now calling' }).getByRole('listitem');
  await expect(now).toHaveText(/A-07\s*Room 3\s*Dr\. Arjun Rao \(synthetic\)/);
  await expect(page.getByRole('region', { name: 'Next' }).getByRole('listitem')).toHaveText([/A-08\s*≈ 10 min/, /B-03\s*≈ 15 min/, /A-09\s*≈ 25 min/]);
  await expect(page.getByText('Emergency? Tell the front desk immediately.')).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: 'Live updates' })).toHaveText('Live updates');

  expect(calls.pairs).toEqual([{ token }]); // the token goes in the body
  expect(page.url()).not.toContain('#'); // and leaves the address at once
  // Its own requests only: never the staff session or anything else that could hold patient data.
  expect(new Set(calls.requests)).toEqual(new Set(['/v1/queue/tv/session', '/v1/queue/tv', '/v1/queue/hub/negotiate', '/v1/queue/hub']));
});

test('a call-in updates the screen without reloading and chimes once the chime is on', async ({ page }) => {
  const { hub, state } = await openTv(page);
  await expect(page.getByRole('region', { name: 'Now calling' })).toContainText('A-07');
  const chime = page.getByRole('button', { name: 'Chime off' });
  await chime.click(); // a press on the screen switches it on (and lets the browser play sound)
  await expect(page.getByRole('button', { name: 'Chime on' })).toHaveAttribute('aria-pressed', 'true');
  const afterSwitch = await tones(page);
  expect(afterSwitch).toBe(2); // the test chime

  state.tv = queue([{ token: 'A-08', room: 'Room 3', doctor: 'Dr. Arjun Rao (synthetic)' }, ...called]);
  hub.push('QueueChanged'); // nothing in the message: the screen fetches its queue again
  await expect(page.getByRole('region', { name: 'Now calling' }).getByRole('listitem')).toHaveCount(2);
  await expect.poll(() => tones(page)).toBe(afterSwitch + 2);

  hub.drop();
  await expect(page.getByRole('status').filter({ hasText: 'Live updates' })).toHaveText('Live updates paused · refreshing every 30 s');
  hub.restore();
  await expect(page.getByRole('status').filter({ hasText: 'Live updates' })).toHaveText('Live updates', { timeout: 20_000 });
});

test('an unknown or revoked link, and a screen never set up, say so', async ({ page }) => {
  await openTv(page, { paired: false });
  await expect(page.getByRole('alert')).toContainText('This screen link is not valid');
  await page.unrouteAll({ behavior: 'ignoreErrors' });
  await openTv(page, { paired: false, link: false });
  await expect(page.getByRole('alert')).toContainText('This screen is not set up');
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const [locale, nowLabel] of [['en', 'Now calling'], ['ar', 'يُنادى الآن']] as const)
  for (const size of [{ width: 1920, height: 1080 }, { width: 1280, height: 720 }])
    test(`reads from 3 metres at ${size.width}×${size.height} (${locale}): 48 px tokens, no scrolling sideways, axe clean`, async ({ page }) => {
      await page.setViewportSize(size);
      await openTv(page, { locale });
      await expect(page.getByRole('heading', { name: nowLabel })).toBeVisible();
      if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
      for (const t of await page.locator('bdi').all()) {
        expect(parseFloat(await t.evaluate(e => getComputedStyle(e).fontSize))).toBeGreaterThanOrEqual(48);
        await expect(t).toHaveAttribute('dir', 'ltr'); // "A-07" reads the same in Arabic
      }
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
          .map(v => `${v.id} (${v.nodes.length})`));
      expect(violations).toEqual([]);
    });

// Staff side: a practice admin adds a display, gets its link once and can revoke it.
const admin = {
  user: { id: 'u-admin', displayName: 'K. Lakshmi' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'PracticeAdmin', flags: [] as string[],
    capabilities: { StaffRolesIntegrations: 'Config', CheckInQueue: 'Read', RegisterBookPatients: 'Full' } },
};

test('a practice admin adds a display, gets its link once and revokes it', async ({ page }) => {
  const displays = [{ id: 'd-1', facilityId: 'f-1', name: 'Old lobby TV', createdAt: '2026-10-01T04:00:00Z', revokedAt: null as string | null }];
  const calls = { created: [] as unknown[], revoked: [] as string[], csrf: [] as (string | undefined)[] };
  await page.route('**/v1/me/context', r => r.fulfill({ json: admin }));
  await page.route('**/v1/queue/displays?*', r => r.fulfill({ json: displays }));
  await page.route('**/v1/queue/displays', r => {
    calls.created.push(r.request().postDataJSON());
    calls.csrf.push(r.request().headers()['x-medvya-csrf']);
    const display = { id: 'd-2', facilityId: 'f-1', name: 'Waiting room TV', createdAt: '2026-10-19T04:00:00Z', revokedAt: null };
    displays.push(display);
    return r.fulfill({ json: { display, token } });
  });
  await page.route('**/v1/queue/displays/*', r => {
    calls.revoked.push(r.request().url().split('/').at(-1)!);
    calls.csrf.push(r.request().headers()['x-medvya-csrf']);
    displays[0]!.revokedAt = '2026-10-19T05:00:00Z';
    return r.fulfill({ status: 204, body: '' });
  });
  await page.goto('/admin/displays');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Queue displays');

  await page.getByRole('button', { name: 'Add display' }).click();
  await expect(page.getByText('Name the screen (up to 60 characters).')).toBeVisible();
  await page.getByLabel('Where is the screen?').fill('Waiting room TV');
  await page.keyboard.press('Enter');
  await expect(page.getByLabel('Open this link once on the screen')).toHaveValue(`http://localhost:${new URL(page.url()).port}/queue/tv#${token}`);
  expect(calls.created).toEqual([{ facilityId: 'f-1', name: 'Waiting room TV' }]);

  await page.getByRole('button', { name: 'Revoke Old lobby TV' }).click();
  await expect(page.getByText(/^Revoked/)).toBeVisible();
  expect(calls.revoked).toEqual(['d-1']);
  expect(calls.csrf).toEqual(['1', '1']);
  for (const b of await page.getByRole('button').all()) if (await b.isVisible()) expect((await b.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  await page.addScriptTag({ path: axePath });
  const violations = await page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
  expect(violations).toEqual([]);
});
