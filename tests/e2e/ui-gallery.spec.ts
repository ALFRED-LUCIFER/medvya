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
