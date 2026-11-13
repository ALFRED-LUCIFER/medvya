import { createRequire } from 'node:module';
import { expect, test } from '@playwright/test';

// MDV-P1-083: Chip, Tag and Banner in the seven tones on /__ui, in English and Arabic, with axe.
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const tones = ['danger', 'warning', 'success', 'info', 'brand', 'ai', 'neutral'];
const draft = { en: 'AI draft · not reviewed', ar: 'مسودة ذكاء اصطناعي · غير مراجَعة' };

for (const locale of ['en', 'ar'] as const) {
  test(`Chip, Tag and Banner in seven tones with words, ai dashed, axe clean (${locale})`, async ({ page }) => {
    await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
    await page.goto('/__ui');
    await expect(page.getByRole('heading', { name: 'Chip, Tag and Banner' })).toBeVisible();
    for (const [group, slot] of [['chips', 'chip'], ['tags', 'tag'], ['banners', 'banner']] as const) {
      const items = page.getByTestId(group).locator(`[data-slot=${slot}]`);
      expect(new Set(await items.evaluateAll(els => els.map(e => e.getAttribute('data-tone'))))).toEqual(new Set(tones));
      for (const el of await items.all()) expect((await el.innerText()).trim().length, `${slot} has words`).toBeGreaterThan(0);
      if (group !== 'banners') {
        await expect(items.filter({ has: page.locator('svg') }).first()).toBeVisible(); // with an icon
        expect(await items.evaluateAll(els => els.filter(e => !e.querySelector('svg')).length)).toBeGreaterThan(0); // and without
      }
      for (const ai of await items.and(page.locator('[data-tone=ai]')).all()) {
        await expect(ai).toContainText(draft[locale]);
        await expect(ai).toHaveCSS('border-top-style', 'dashed');
      }
    }
    expect(await page.locator('html').getAttribute('dir')).toBe(locale === 'ar' ? 'rtl' : 'ltr');
    await page.addScriptTag({ path: axePath });
    expect(await page.evaluate(async () =>
      (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string }[] }> } }).axe.run()).violations.map(v => v.id))).toEqual([]);
  });
}
