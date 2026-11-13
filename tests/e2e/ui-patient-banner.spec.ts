import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-081: PatientBanner on /__ui at four widths, in English and Arabic, with axe. Synthetic patients.
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const longName = 'Venkata Lakshmi Narasimha Subramaniam Ramachandran-Iyengar';
const words = {
  en: { first: 'Allergy: Penicillin (rash)', second: 'Allergy: Sulfonamides', more: '+3 more', all: 'Allergies', patient: (n: string) => `Patient ${n}` },
  ar: { first: 'حساسية: Penicillin (rash)', second: 'حساسية: Sulfonamides', more: '+3 أخرى', all: 'الحساسية', patient: (n: string) => `المريض ${n}` },
};
const open = async (page: Page, locale: string) => {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.goto('/__ui');
  await expect(page.getByRole('heading', { name: 'Patient banner', exact: true })).toBeVisible();
};
const axe = async (page: Page) => {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string }[] }> } }).axe.run()).violations.map(v => v.id));
};

for (const locale of ['en', 'ar'] as const) {
  for (const width of [1440, 1024, 768, 375]) {
    test(`strip with five allergies and a long name: readable, nothing clipped, axe clean (${locale}, ${width} px)`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await open(page, locale);
      const w = words[locale];
      const strip = page.locator('[data-slot=patient-banner][data-variant=strip]').filter({ hasText: longName });
      await expect(strip.locator('[data-slot=allergy]')).toHaveText([w.first, w.second], { useInnerText: true });
      for (const a of await strip.locator('[data-slot=allergy]').all()) await expect(a).toBeVisible();

      // Nothing is cut off and no ellipsis stands alone: every cell's content fits its box, inside the viewport.
      const clipped = await page.locator('[data-slot=patient-banner]').evaluateAll(banners => banners.flatMap(b =>
        [b, ...b.querySelectorAll('*')].filter(e => {
          const s = getComputedStyle(e), r = e.getBoundingClientRect();
          return e.scrollWidth > e.clientWidth + 1 || s.textOverflow === 'ellipsis' || r.right > innerWidth + 1 || r.left < -1;
        }).map(e => e.outerHTML.slice(0, 80))));
      expect(clipped).toEqual([]);

      // AC5: the layout mirrors: in Arabic the name sits right of the age.
      const name = (await strip.getByRole('heading').boundingBox())!, age = (await strip.locator('[data-field=ageSex]').boundingBox())!;
      if (name.y === age.y) expect(locale === 'ar' ? name.x > age.x : name.x < age.x).toBe(true);
      expect(await axe(page)).toEqual([]);

      // The count opens the full list.
      await strip.getByRole('button', { name: w.more }).click();
      const dialog = page.getByRole('dialog', { name: w.all });
      await expect(dialog.locator('[data-slot=allergy]')).toHaveCount(5);
    });
  }

  test(`desk holds no clinical text; the allergy states have their own words (${locale})`, async ({ page }) => {
    await open(page, locale);
    const desk = page.getByRole('region', { name: words[locale].patient('Lakshmi Pillai') });
    await expect(desk).toBeVisible();
    await expect(desk).not.toContainText(/Allerg|حساسية|الحساسية|Penicillin|consent|موافقة/i);
    await expect(desk.locator('[data-slot=allergy], [data-slot=allergy-state]')).toHaveCount(0);
    await expect(page.getByRole('region', { name: words[locale].patient('Noura Al Hashimi') }).locator('[data-slot=allergy-state]')).toHaveClass(/bg-warning-bg/);
    await expect(page.getByRole('region', { name: words[locale].patient('Rahul Varma') }).locator('[data-slot=allergy-state]')).toHaveClass(/text-neutral-text/);
  });
}

test('the pinned strip stays in view while its page scrolls', async ({ page }) => {
  await open(page, 'en');
  const box = page.getByTestId('pinned-box');
  await box.scrollIntoViewIfNeeded();
  await box.evaluate(e => e.scrollTo(0, 200));
  const [b, s] = [(await box.boundingBox())!, (await page.getByRole('region', { name: 'Patient Priya Raman' }).boundingBox())!];
  expect(Math.abs(s.y - b.y)).toBeLessThan(2);
});

// AC6: 400 % zoom of a 1280 by 1024 window is a 320 by 256 CSS-pixel viewport.
test.describe('at 400 % zoom', () => {
  test.use({ viewport: { width: 320, height: 256 } });
  test('the pinned strip un-pins and scrolls with the content', async ({ page }) => {
    await open(page, 'en');
    const strip = page.getByRole('region', { name: 'Patient Priya Raman' });
    await expect(strip).toHaveCSS('position', 'static');
    const box = page.getByTestId('pinned-box');
    await box.scrollIntoViewIfNeeded();
    const before = (await strip.boundingBox())!.y;
    await box.evaluate(e => e.scrollTo(0, 120));
    await expect.poll(async () => (await strip.boundingBox())!.y).toBeLessThan(before - 50);
  });
});
