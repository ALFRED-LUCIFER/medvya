import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';

// MDV-P1-079: the /__ui token specimen measures each type token in English and Arabic; axe finds nothing.
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const measured = (page: import('@playwright/test').Page, cls: string) =>
  page.locator('code', { hasText: new RegExp(`^${cls}$`) }).locator('xpath=following-sibling::span[1]');

for (const locale of ['en', 'ar'] as const) {
  test(`token specimen in /__ui (${locale}): measured sizes, Arabic font, axe clean`, async ({ page }) => {
    await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
    await page.goto('/__ui');
    await expect(page.getByRole('heading', { name: 'Type tokens' })).toBeVisible();
    await expect(measured(page, 'text-body-large')).toHaveText('16px / 24px · 500');
    await expect(measured(page, 'text-display')).toHaveText('30px / 36px · 800');
    await expect(measured(page, 'w-control')).toHaveText('44px');
    await expect(measured(page, 'max-w-page')).toHaveText('1600px');
    expect(await page.locator('html').getAttribute('dir')).toBe(locale === 'ar' ? 'rtl' : 'ltr');
    expect(await page.evaluate(() => getComputedStyle(document.body).fontFamily)).toContain('Noto Sans Arabic');
    await page.addScriptTag({ path: axePath });
    const violations = await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string }[] }> } }).axe.run()).violations.map(v => v.id));
    expect(violations).toEqual([]);
  });
}

// MDV-P1-077: the library defaults, measured on /__ui at a 1024 px touch viewport, in English and Arabic.
test.describe('library control defaults', () => {
  test.use({ viewport: { width: 1024, height: 768 }, hasTouch: true });
  for (const locale of ['en', 'ar']) {
    test(`library controls are 44 px by default and axe-clean on /__ui (${locale})`, async ({ page }) => {
      await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
      await page.goto('/__ui');
      if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
      const controls = page.locator('[data-measured] :is(input, textarea, button), [data-slot=choice-row]');
      await expect(controls).toHaveCount(7); // input, select, textarea, switch, three choice rows
      for (const c of await controls.all()) {
        const box = (await c.boundingBox())!;
        expect(box.height, await c.evaluate(e => e.outerHTML.slice(0, 80))).toBeGreaterThanOrEqual(43.5);
      }
      // The switch: a 44 by 44 target, while the visible track stays 32 by about 18 px.
      const sw = (await page.getByRole('switch').boundingBox())!;
      expect(sw.width).toBeGreaterThanOrEqual(43.5);
      const track = (await page.locator('[data-slot=switch-track]').boundingBox())!;
      expect([Math.round(track.width), Math.round(track.height)]).toEqual([32, 18]);
      // The table's scroll region has a name.
      await expect(page.getByRole('region', { name: "Today's queue" })).toBeVisible();
      await page.addScriptTag({ path: axePath });
      expect(await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string }[] }> } }).axe.run()).violations.map(v => v.id))).toEqual([]);
      // Options are 44 px on a coarse pointer.
      await page.locator('#d-select').click();
      for (const o of await page.getByRole('option').all()) expect((await o.boundingBox())!.height).toBeGreaterThanOrEqual(43.5);
    });
  }
});
