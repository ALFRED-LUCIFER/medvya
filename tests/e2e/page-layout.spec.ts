import { expect, test } from '@playwright/test';
import { admin, openScreen, reception, type Persona } from './support/desk';

// UX-01 (issue #1113): every page sits in a PageContainer of one of three named widths and has one PageHeader size.
// Measured, not pixel-compared: a screenshot baseline would break on every font or OS change, while these numbers are what the audit found wrong.
const screens: { name: string; path: string; as: Persona; width: 'fluid' | 'form' | 'reading' }[] = [
  { name: 'board', path: '/front-desk', as: reception, width: 'fluid' },
  { name: 'walk-in', path: '/front-desk/walk-in', as: reception, width: 'form' },
  { name: 'settings', path: '/settings', as: admin, width: 'reading' },
];
const limit = { form: 768, reading: 1152 }; // max-w-3xl, max-w-6xl

for (const { w, h } of [{ w: 1920, h: 1080 }, { w: 1366, h: 768 }]) {
  for (const s of screens) {
    test(`${s.name} at ${w}x${h} uses a ${s.width} container and the shared title size`, async ({ page }) => {
      await page.setViewportSize({ width: w, height: h });
      await openScreen(page, s, 'en');
      const main = await page.locator('main').evaluate(el => {
        const r = el.getBoundingClientRect(), cs = getComputedStyle(el);
        return r.width - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
      });
      const container = page.locator('[data-slot=page-container]').first();
      await expect(container).toHaveAttribute('data-width', s.width);
      const box = (await container.boundingBox())!;
      if (s.width === 'fluid') expect(box.width).toBeGreaterThanOrEqual(main - 1);
      else expect(box.width).toBeLessThanOrEqual(limit[s.width] + 1);
      await expect(page.locator('main h1').first()).toHaveCSS('font-size', '24px');
    });
  }
}
