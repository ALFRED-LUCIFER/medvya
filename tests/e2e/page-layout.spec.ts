import { expect, test } from '@playwright/test';
import { admin, openScreen, reception, screens as deskScreens, type Persona } from './support/desk';

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
      await expect(page.locator('main h1').first()).toHaveCSS('font-size', '20px');
    });
  }
}

// MDV-P2-059 AC1 and AC6: every page writes its one h1 in PageHeader's 44 px row, at the same height on every route, in English and Arabic.
// Measured from the top of <main>, so the bound is the shell's: 82 px below a 48 px top bar is content by 130 px (DESIGN.md Layout).
// shortcut: the board and the clinic overview wait for MDV-P1-095 and the desk record for MDV-P1-092, which own those files; add them here when they merge.
const waiting = ['reception board', 'clinic overview', 'patient record'];
for (const locale of ['en', 'ar']) {
  test(`every ${locale} page puts its one title in the same 44 px header row`, async ({ page }) => {
    test.setTimeout(120_000);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.route('**/v1/waitlist?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', now: '2026-10-19T05:02:00Z', entries: [] } }));
    const tops = new Map<string, number>();
    for (const s of [...deskScreens.filter(s => !waiting.includes(s.name)), { name: 'waitlist', path: '/front-desk/waitlist', as: reception }]) {
      await openScreen(page, s, locale);
      await expect(page.locator('main h1'), s.name).toHaveCount(1);
      const row = page.locator('main [data-slot=page-header-row]');
      await expect(row, s.name).toHaveCount(1);
      const box = (await row.boundingBox())!, main = (await page.locator('main').boundingBox())!;
      expect(box.height, s.name).toBe(44);
      expect(box.y + box.height + 12 - main.y, s.name).toBeLessThanOrEqual(82);
      tops.set(s.name, box.y);
    }
    expect(new Set(tops.values()).size, JSON.stringify(Object.fromEntries(tops))).toBe(1);
  });
}
