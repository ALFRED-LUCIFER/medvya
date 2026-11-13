import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-082: the layout footer and SaveStatus on /__ui, in English and Arabic, with axe.
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
const words = {
  en: { saving: 'Saving…', saved: 'Saved 10:42', failed: 'Not saved. Trying again.' },
  ar: { saving: 'جارٍ الحفظ…', saved: 'تم الحفظ 10:42', failed: 'لم يتم الحفظ. نحاول مرة أخرى.' },
};
const open = async (page: Page, locale: string) => {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.goto('/__ui');
  await expect(page.getByRole('heading', { name: 'Sticky action bar and save status' })).toBeVisible();
};
const footer = (page: Page) => page.locator('[data-slot=sticky-action-bar]');
const axe = async (page: Page) => {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string }[] }> } }).axe.run()).violations.map(v => v.id));
};

for (const locale of ['en', 'ar'] as const) {
  test(`footer never hides a field or a focused control; SaveStatus reads its three states (${locale})`, async ({ page }) => {
    await open(page, locale);

    // AC1: the last field, scrolled to, is whole above the pinned footer.
    const last = page.getByLabel('Follow-up');
    await last.evaluate(e => e.scrollIntoView({ block: 'nearest' }));
    const [field, bar] = [(await last.boundingBox())!, (await footer(page).boundingBox())!];
    expect(Math.round(bar.height)).toBeGreaterThanOrEqual(56);
    expect(field.y + field.height).toBeLessThanOrEqual(bar.y + 0.5);

    // AC2: Tab through the whole page; every focused element is inside the viewport and none is under the footer.
    await page.evaluate(() => window.scrollTo(0, 0));
    let seen = 0;
    for (let i = 0; i < 120; i++) {
      await page.keyboard.press('Tab');
      const r = await page.evaluate(() => {
        const el = document.activeElement as HTMLElement | null;
        if (!el || el === document.body) return null;
        const b = el.getBoundingClientRect(), f = document.querySelector('[data-slot=sticky-action-bar]')!;
        const fb = f.getBoundingClientRect();
        return {
          what: `${el.outerHTML.slice(0, 60)} at ${Math.round(b.top)}-${Math.round(b.bottom)}, footer ${Math.round(fb.top)}, viewport ${innerHeight}`,
          inView: b.top >= -0.5 && b.left >= -0.5 && b.bottom <= innerHeight + 0.5 && b.right <= innerWidth + 0.5,
          underFooter: !f.contains(el) && b.bottom > fb.top + 0.5 && b.top < fb.bottom,
        };
      });
      if (!r) break; // focus left the page: every control has been visited
      seen++;
      expect(r.inView, r.what).toBe(true);
      expect(r.underFooter, r.what).toBe(false);
    }
    expect(seen).toBeGreaterThan(20);

    // AC3 and AC4: one live region that stays mounted; the words swap with no animation under reduced motion.
    const status = footer(page).getByRole('status');
    await expect(status).toHaveText('');
    await status.evaluate(e => { (e as HTMLElement & { marked?: boolean }).marked = true; });
    for (const [button, text] of [['Show saving', words[locale].saving], ['Show saved at 10:42', words[locale].saved], ['Show not saved', words[locale].failed]] as const) {
      await page.getByRole('button', { name: button }).click();
      await expect(status).toHaveText(text);
    }
    expect(await status.evaluate(e => (e as HTMLElement & { marked?: boolean }).marked)).toBe(true);
    expect(await status.locator('span').evaluate(e => getComputedStyle(e).animationName)).toBe('none');

    // AC6
    expect(await axe(page)).toEqual([]);
  });
}

test('SaveStatus fades in over 150 ms by opacity only when motion is allowed', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'no-preference' });
  await open(page, 'en');
  await page.getByRole('button', { name: 'Show saving' }).click();
  const s = await footer(page).getByRole('status').locator('span').evaluate(e => {
    const c = getComputedStyle(e);
    return { duration: c.animationDuration, translate: c.getPropertyValue('--tw-enter-translate-y').trim() || '0', scale: c.getPropertyValue('--tw-enter-scale').trim() || '1' };
  });
  expect(s).toEqual({ duration: '0.15s', translate: '0', scale: '1' });
});

// AC5: 400 % zoom of a 1280 by 1024 window is a 320 by 256 CSS-pixel viewport; the footer then scrolls with the content.
test.describe('at 400 % zoom', () => {
  test.use({ viewport: { width: 320, height: 256 } });
  test('the footer un-pins and scrolls with the page', async ({ page }) => {
    await open(page, 'en');
    await expect(footer(page)).toHaveCSS('position', 'static');
    await page.getByLabel('Plan').scrollIntoViewIfNeeded();
    const before = (await footer(page).boundingBox())!.y;
    await page.mouse.wheel(0, 100);
    await expect.poll(async () => (await footer(page).boundingBox())!.y).toBeLessThan(before - 50);
  });
});
