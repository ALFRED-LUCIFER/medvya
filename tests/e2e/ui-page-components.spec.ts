import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-086: Card, PageMessage, SegmentedControl, SplitLayout and Sheet on /__ui, in English and Arabic, with axe.
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string }[] }> } }).axe.run()).violations.map(v => v.id));
}
async function open(page: Page, locale: string) {
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.goto('/__ui');
  await expect(page.locator('html')).toHaveAttribute('dir', locale === 'ar' ? 'rtl' : 'ltr');
  await page.locator('[data-slot=segmented-control]').scrollIntoViewIfNeeded();
}

for (const locale of ['en', 'ar'] as const) {
  test.describe(`page components (${locale})`, () => {
    test('at 1024: messages, keyboard view switch, side pane beside main; axe clean', async ({ page }) => {
      await page.setViewportSize({ width: 1024, height: 768 });
      await open(page, locale);

      // AC1: busy, error, empty and denied each show an icon, a sentence and at most one action.
      const messages = page.locator('[data-slot=page-message]');
      await expect(messages).toHaveCount(4);
      for (const m of await messages.all()) {
        await expect(m.locator('svg[aria-hidden]')).toHaveCount(1);
        await expect(m.locator('p').last()).not.toBeEmpty(); // the sentence; an optional title line sits above it
        expect(await m.getByRole('button').count()).toBeLessThanOrEqual(1);
      }
      if (locale === 'ar') await expect(messages.first().locator('p')).not.toHaveText('Loading Front desk…');

      // AC2: Tab moves between segments, Enter selects, the selected one is pressed, each is at least 44 px tall.
      const segments = page.locator('[data-slot=segmented-control] button');
      await expect(segments).toHaveCount(3);
      for (const s of await segments.all()) expect((await s.boundingBox())!.height).toBeGreaterThanOrEqual(43.5);
      await segments.nth(0).focus();
      await page.keyboard.press('Tab');
      await expect(segments.nth(1)).toBeFocused();
      await page.keyboard.press('Enter');
      await expect(segments.nth(1)).toHaveAttribute('aria-pressed', 'true');
      await expect(segments.nth(0)).toHaveAttribute('aria-pressed', 'false');

      // AC4 at 1024: the side pane is beside the main pane (after it in reading order: right in English, left in Arabic), no opener.
      const split = page.getByTestId('split');
      const aside = split.getByRole('complementary');
      await expect(aside).toBeVisible();
      await expect(split.locator('> div').first().getByRole('button')).toBeHidden(); // the opener is for narrow screens only
      const main = (await split.locator('> div').first().boundingBox())!, side = (await aside.boundingBox())!;
      expect(Math.round(side.width)).toBe(320);
      if (locale === 'en') expect(side.x).toBeGreaterThan(main.x + main.width - 1);
      else expect(side.x + side.width).toBeLessThan(main.x + 1);

      expect(await axe(page)).toEqual([]);
    });

    test('at 768: the side pane is a Sheet from the end side that traps focus, closes on Escape, returns focus; axe clean open', async ({ page }) => {
      await page.setViewportSize({ width: 768, height: 1024 });
      await open(page, locale);
      const split = page.getByTestId('split');
      await expect(split.getByRole('complementary')).toHaveCount(0);
      const opener = split.getByRole('button');
      await opener.click();
      const sheet = page.getByRole('dialog');
      await expect(sheet).toBeVisible();
      await expect(sheet.getByText('Penicillin (rash)')).toBeVisible();

      // AC6: it opens from the end side: the right edge in English, the left in Arabic.
      const box = (await sheet.boundingBox())!;
      if (locale === 'en') expect(Math.round(box.x + box.width)).toBe(768);
      else expect(Math.round(box.x)).toBe(0);

      // AC4: focus stays inside the sheet.
      for (let i = 0; i < 4; i++) {
        await page.keyboard.press('Tab');
        expect(await sheet.evaluate(el => el.contains(document.activeElement))).toBe(true);
      }
      expect(await axe(page)).toEqual([]);

      await page.keyboard.press('Escape');
      await expect(sheet).toBeHidden();
      await expect(opener).toBeFocused();
      expect(await axe(page)).toEqual([]);
    });
  });
}

// AC5: with reduced motion the sheet only fades (and the global rule makes it instant); without it, it also slides.
for (const reducedMotion of ['reduce', 'no-preference'] as const) {
  test(`sheet motion with prefers-reduced-motion: ${reducedMotion}`, async ({ page }) => {
    await page.emulateMedia({ reducedMotion });
    await page.setViewportSize({ width: 768, height: 1024 });
    await open(page, 'en');
    await page.getByTestId('split').getByRole('button').click();
    const sheet = page.getByRole('dialog');
    await expect(sheet).toBeVisible();
    const slide = await sheet.evaluate(el => getComputedStyle(el).getPropertyValue('--tw-enter-translate-x').trim());
    if (reducedMotion === 'reduce') {
      expect(['', '0']).toContain(slide); // unset (a registered property reads its initial 0)
      expect(await sheet.evaluate(el => getComputedStyle(el).animationName)).toBe('none');
    } else expect(slide).toBe('100%');
  });
}
