import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-085: Field and ErrorSummary on the /__ui sample form, at 375 and 1440 px, in English and Arabic, with axe.
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string }[] }> } }).axe.run()).violations.map(v => v.id));
}

for (const locale of ['en', 'ar'] as const) {
  for (const width of [375, 1440]) {
    test(`failed submit (${locale}, ${width} px): the summary takes focus in view, links focus fields, axe clean`, async ({ page }) => {
      await page.setViewportSize({ width, height: 800 });
      await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
      await page.goto('/__ui');
      await expect(page.locator('html')).toHaveAttribute('dir', locale === 'ar' ? 'rtl' : 'ltr');
      const required = locale === 'en' ? '(required)' : '(مطلوب)';

      // AC4: required fields carry the word in the label; no asterisk stands alone.
      await expect(page.getByLabel(`Full name ${required}`)).toBeVisible();
      await expect(page.getByLabel('Email', { exact: true })).toBeVisible();
      expect(await page.locator('form label').allTextContents()).not.toContainEqual(expect.stringContaining('*'));

      await page.getByRole('button', { name: 'Save patient' }).click();

      // AC1, AC2, AC5: focus moves to the summary (announced once, by focus), which is in the viewport and lists three links.
      const summary = page.getByRole('group', { name: 'There is a problem' });
      await expect(summary).toBeFocused();
      await expect(summary).toBeInViewport();
      await expect(summary).not.toHaveAttribute('aria-live', /./);
      const links = summary.getByRole('link');
      await expect(links).toHaveText(['Enter the full name', 'Enter a 10-digit mobile number', 'Enter a 6-digit PIN code']);

      // AC3: the error is text beside the field, linked to the control, and the control is marked invalid.
      const mobile = page.locator('#fg-mobile');
      await expect(mobile).toHaveAttribute('aria-invalid', 'true');
      await expect(mobile).toHaveAccessibleDescription(/Enter a 10-digit mobile number/);

      // Typing does not pull focus back to the summary.
      await links.nth(1).click();
      await expect(mobile).toBeFocused();
      await page.keyboard.type('98');
      await expect(mobile).toBeFocused();

      // A link activated by keyboard focuses its field.
      await summary.focus();
      await page.keyboard.press('Tab');
      await page.keyboard.press('Enter');
      await expect(page.locator('#fg-name')).toBeFocused();

      expect(await axe(page)).toEqual([]);
    });
  }
}
