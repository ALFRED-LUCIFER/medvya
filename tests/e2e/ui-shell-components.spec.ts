import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-084: AccountMenu, NavRailItem, PageHeader and Tooltip on /__ui, in English and Arabic, with axe.
const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
// With the menu open the rest of the page is hidden from assistive technology (modal menu), so the open menu is checked on its own.
async function axe(page: Page, scope = 'html') {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async s =>
    (await (window as unknown as { axe: { run: (c: Element) => Promise<{ violations: { id: string }[] }> } }).axe.run(document.querySelector(s)!)).violations.map(v => v.id), scope);
}

const names = {
  en: { account: 'Signed in as Meera Iyer', items: ['Language', 'My devices', 'Change facility', 'Sign out'] },
  ar: { account: /Meera Iyer/, items: null },
};

for (const locale of ['en', 'ar'] as const) {
  test(`shell components (${locale}): menu keyboard path, labelled rail, steady header, tooltip on focus, axe clean`, async ({ page }) => {
    await page.setViewportSize({ width: 1024, height: 900 });
    await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
    await page.goto('/__ui');
    await expect(page.locator('html')).toHaveAttribute('dir', locale === 'ar' ? 'rtl' : 'ltr');

    // AC3: every rail label is visible text under its icon and not clipped, in the longest names of the language.
    const items = page.getByRole('navigation', { name: 'Rail sample' }).locator('[data-slot=nav-rail-item]');
    await expect(items).toHaveCount(7);
    for (const item of await items.all()) {
      const label = item.locator('span');
      await expect(label).toBeVisible();
      const [icon, text] = [(await item.locator('svg').boundingBox())!, (await label.boundingBox())!];
      expect(text.y).toBeGreaterThanOrEqual(icon.y + icon.height - 1);
      expect(await label.evaluate(el => el.scrollWidth <= el.clientWidth && el.scrollHeight <= el.clientHeight + 1)).toBe(true);
      const box = (await item.boundingBox())!;
      expect(box.width).toBeLessThanOrEqual(72);
      expect(text.x).toBeGreaterThanOrEqual(box.x - 0.5); // the label stays inside the item: wrapped, not spilling over or cut
      expect(text.x + text.width).toBeLessThanOrEqual(box.x + box.width + 0.5);
    }

    // AC4: the date arrives after loading; the title and actions do not move and the row stays 44 px.
    const header = page.getByTestId('header-sample');
    const row = header.locator('[data-slot=page-header-row]');
    const title = header.getByRole('heading', { level: 1 });
    const load = header.getByRole('button', { name: 'Load the date' });
    await header.scrollIntoViewIfNeeded();
    const before = [await row.boundingBox(), await title.boundingBox(), await load.boundingBox()];
    await load.click();
    await expect(header.locator('[data-slot=page-header-meta]')).toHaveText('Fri 13 Nov 2026');
    const after = [await row.boundingBox(), await title.boundingBox(), await load.boundingBox()];
    expect(after).toEqual(before);
    expect(Math.round(after[0]!.height)).toBe(44);
    await expect(title).toHaveCSS('font-size', '20px');

    // AC5: keyboard focus shows the tooltip; the button is named without it.
    const bell = page.getByTestId('tip');
    await bell.focus();
    await expect(page.getByRole('tooltip')).toBeVisible();
    expect(await bell.getAttribute('aria-label')).toBeTruthy();
    await bell.blur();

    // AC1, AC2: Enter opens the menu; arrows move through language, my devices, change facility, sign out; Escape returns focus.
    const avatar = page.getByRole('button', { name: names[locale].account });
    await avatar.focus();
    await page.keyboard.press('Enter');
    const menu = page.getByRole('menu');
    await expect(menu).toBeVisible();
    const menuItems = menu.getByRole('menuitem');
    await expect(menuItems).toHaveCount(4);
    if (names[locale].items) await expect(menuItems).toHaveText(names[locale].items!);
    for (let i = 0; i < 4; i++) {
      if (i > 0) await page.keyboard.press('ArrowDown');
      else await expect(menuItems.nth(0)).toBeFocused();
      await expect(menuItems.nth(i)).toBeFocused();
      expect((await menuItems.nth(i).boundingBox())!.height).toBeGreaterThanOrEqual(43.5);
    }
    const signOut = (await menuItems.nth(3).boundingBox())!, separator = (await menu.getByRole('separator').last().boundingBox())!;
    expect(separator.y).toBeLessThan(signOut.y);
    expect(separator.y).toBeGreaterThan((await menuItems.nth(2).boundingBox())!.y);
    expect(await axe(page, '[role=menu]')).toEqual([]);
    await page.keyboard.press('Escape');
    await expect(menu).toBeHidden();
    await expect(avatar).toBeFocused();

    expect(await axe(page)).toEqual([]);
  });
}
