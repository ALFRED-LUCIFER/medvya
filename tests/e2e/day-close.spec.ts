import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { billingDesk } from './support/billing';

// MDV-P4-012 (P-07, P-13): the day close. CI has no API, so the cash-session endpoints are mocked in the shapes the server sends (CashDayView).
// The second-person rule is the server's: the mock answers mayConfirm as the server would for the counter (false) or a colleague (true). A counted drawer
// waits in `awaiting` while the next one may open.

const denominations = [50_000, 20_000, 10_000, 5_000, 2_000, 1_000, 500, 200, 100];

async function mockCashDay(page: Page, { locale = 'en', colleague = false, open = false }: { locale?: string; colleague?: boolean; open?: boolean } = {}) {
  const session = () => ({
    id: 'cs-1', state: 'Open', currency: 'INR', openingFloatMinor: 100_000, openedByName: 'A. Joseph (synthetic)', openedAt: '2026-10-19T03:30:00Z',
    counts: [] as { valueMinor: number; count: number }[], totals: [{ method: 'Cash', payments: 3, amountMinor: 60_000 }, { method: 'UpiDirect', payments: 2, amountMinor: 40_000 }],
    expectedMinor: 160_000, countedMinor: null as number | null, varianceMinor: null as number | null, note: null as string | null, countedByName: null as string | null,
    countedAt: null as string | null, confirmedByName: null as string | null, closedAt: null as string | null, mayConfirm: false, adjustments: [], version: 1,
  });
  type Session = ReturnType<typeof session>;
  const day = { current: open ? session() : null as Session | null, awaiting: [] as Session[], denominations, currency: 'INR', recent: [] as Session[] };
  const posts: { path: string; body: unknown }[] = [];
  const close = (s: Session, confirmedBy: string | null) => {
    Object.assign(s, { state: 'Closed', closedAt: '2026-10-19T13:35:00Z', confirmedByName: confirmedBy, mayConfirm: false });
    day.recent.unshift(s);
    day.awaiting = day.awaiting.filter(a => a !== s);
  };
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: billingDesk }));
  await page.route('**/v1/notifications/inbox?*', r => r.fulfill({ json: { unread: 0, items: [] } }));
  await page.route('**/v1/billing/cash-sessions', r => {
    if (r.request().method() === 'GET') return r.fulfill({ json: day });
    posts.push({ path: 'open', body: r.request().postDataJSON() });
    day.current = session();
    return r.fulfill({ json: day.current });
  });
  await page.route('**/v1/billing/cash-sessions/cs-1/count', r => {
    const body = r.request().postDataJSON() as { counts: { valueMinor: number; count: number }[]; note: string | null };
    posts.push({ path: 'count', body });
    const s = day.current!;
    const counted = body.counts.reduce((n, c) => n + c.valueMinor * c.count, 0);
    Object.assign(s, { counts: body.counts, countedMinor: counted, varianceMinor: counted - s.expectedMinor, note: body.note, countedByName: 'A. Joseph (synthetic)',
      countedAt: '2026-10-19T13:30:00Z', state: 'AwaitingConfirmation', mayConfirm: colleague, version: s.version + 1 });
    day.current = null; // counted: the next drawer may open while this one waits
    if (counted === s.expectedMinor) close(s, null);
    else day.awaiting.push(s);
    return r.fulfill({ json: s });
  });
  await page.route('**/v1/billing/cash-sessions/cs-1/confirm', r => {
    posts.push({ path: 'confirm', body: r.request().postDataJSON() });
    const s = day.awaiting[0];
    if (!s) return r.fulfill({ status: 400, json: { errors: { state: ['invalid'] } } });
    close(s, 'Priya N. (synthetic)');
    return r.fulfill({ json: s });
  });
  await page.route('**/v1/billing/cash-sessions/cs-1/report/pdf', r => r.fulfill({ contentType: 'application/pdf', body: '%PDF-1.7 synthetic' }));
  return { posts };
}

async function countShort(page: Page) {
  await page.getByLabel('₹500.00').fill('3'); // ₹1,500 against ₹1,600 expected: ₹100 short
}

test('the desk opens the drawer, counts it short, and must add a note and wait for a second staff member', async ({ page }) => {
  const api = await mockCashDay(page);
  await page.goto('/billing/day-close');
  await page.getByLabel('Opening float').fill('1000');
  await page.getByRole('button', { name: 'Open drawer' }).click();
  await expect(page.getByText('Expected in drawer').first()).toBeVisible();
  await expect(page.getByText('₹1,600.00').first()).toBeVisible();

  await page.getByLabel('₹500.00').fill('2');
  await page.getByLabel('₹200.00').fill('2'); // ₹1,400: ₹200 short
  await expect(page.getByText('Attention · cash short ₹200.00')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Send for confirmation' })).toBeDisabled();
  await page.getByLabel('Variance note').fill('₹200 change given from personal cash');
  await page.getByRole('button', { name: 'Send for confirmation' }).click();
  await expect(page.getByText('Ask a colleague to sign in on their own account and confirm.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Confirm and close day' })).toHaveCount(0); // the counter never confirms their own count
  await expect(page.getByRole('button', { name: 'Open drawer' })).toBeVisible(); // the next drawer may open meanwhile
  expect(api.posts.map(p => p.body)).toEqual([
    { openingFloatMinor: 100_000 },
    { counts: denominations.map(d => ({ valueMinor: d, count: d === 50_000 || d === 20_000 ? 2 : 0 })), note: '₹200 change given from personal cash', version: 1 },
  ]);
});

test('a colleague confirms the variance; the closed day gives a shift report', async ({ page }) => {
  await mockCashDay(page, { open: true, colleague: true });
  await page.goto('/billing/day-close');
  await countShort(page);
  await page.getByLabel('Variance note').fill('Change given from personal cash');
  await page.getByRole('button', { name: 'Send for confirmation' }).click();
  await page.getByRole('button', { name: 'Confirm and close day' }).click();
  const row = page.getByRole('row', { name: /A\. Joseph/ });
  await expect(row).toContainText('Priya N. (synthetic)');
  const download = page.waitForEvent('download');
  await row.getByRole('button', { name: /Shift report/ }).click();
  expect((await download).suggestedFilename()).toBe('shift-report.pdf');
  await expect(page.getByRole('button', { name: 'Open drawer' })).toBeVisible(); // the next shift
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  for (const view of ['open', 'count', 'awaiting'] as const)
    test(`axe finds no violations on day close, ${view} (${locale})`, async ({ page }) => {
      await mockCashDay(page, { locale, open: view !== 'open', colleague: true });
      await page.goto('/billing/day-close');
      if (view !== 'open') {
        await page.locator('#day-close-count-50000').fill('3');
        await expect(page.locator('[data-slot="banner"][data-tone="warning"]')).toBeVisible();
      } else await expect(page.locator('#day-close-float')).toBeVisible();
      if (view === 'awaiting') {
        await page.locator('#day-close-note').fill('Synthetic note');
        await page.locator('footer').getByRole('button').first().click(); // Send for confirmation
        await expect(page.locator('[data-slot="banner"][data-tone="info"]')).toBeVisible(); // the waiting count, with the next drawer's float form
        await expect(page.locator('#day-close-float')).toBeVisible();
      }
      if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
      expect(violations).toEqual([]);
    });
