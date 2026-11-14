import { expect, test, type Page } from '@playwright/test';
import { axe, openScreen, reception, screens as deskScreens, signIn, t } from './support/desk';
import { taskScreens } from './support/oneScreen';

// MDV-P2-057: the type and width tokens on the screens people use most. Measured in the browser, not pixel-compared.
const screens = [
  ...taskScreens.map(s => ({ name: s.name, open: s.open })),
  ...deskScreens.filter(s => !taskScreens.some(x => x.name === s.name)).map(s => ({ name: s.name, open: (page: Page, locale: string) => openScreen(page, s, locale) })),
];
const byName = (name: string) => screens.find(s => s.name === name)!;

// The nine type tokens render at these sizes (packages/tokens: caption 12, mono 13, body and label 14, subheading and body-large 16, heading 18,
// title 20, display 30).
const tokenSizes = ['12px', '13px', '14px', '16px', '18px', '20px', '30px'];

/** Font sizes of every visible element that holds text of its own, with one example each. */
const renderedSizes = (page: Page) => page.evaluate(() => {
  const seen: Record<string, string> = {};
  for (const el of Array.from(document.querySelectorAll<HTMLElement>('body *'))) {
    if (!Array.from(el.childNodes).some(n => n.nodeType === Node.TEXT_NODE && n.textContent!.trim())) continue;
    const r = el.getBoundingClientRect();
    if (!r.width || !r.height) continue;
    seen[getComputedStyle(el).fontSize] ??= `${el.tagName.toLowerCase()}: ${el.textContent!.trim().slice(0, 40)}`;
  }
  return seen;
});

// AC1: only token sizes at 1440 (the queue display and print pages are not in this list, by design).
for (const s of screens)
  test(`${s.name} at 1440 renders only the type token sizes`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await s.open(page, 'en');
    const sizes = await renderedSizes(page);
    expect(Object.entries(sizes).filter(([size]) => !tokenSizes.includes(size))).toEqual([]);
  });

// AC3: at 1920 no paragraph is wider than about 72 characters and the page content is at most 1,600 px, in English and Arabic.
for (const locale of ['en', 'ar'])
  for (const name of ['my day', 'patient record', 'settings profile'])
    test(`${locale} ${name} at 1920 keeps paragraphs to about 72 characters and the page to 1,600 px, and passes axe`, async ({ page }) => {
      await page.setViewportSize({ width: 1920, height: 1080 });
      await byName(name).open(page, locale);
      const widest = await page.evaluate(() => {
        const probe = document.body.appendChild(document.createElement('span'));
        probe.textContent = '0'.repeat(10);
        let max = { ch: 0, text: '' };
        for (const p of Array.from(document.querySelectorAll<HTMLElement>('main p'))) {
          if (!p.textContent!.trim() || !p.getBoundingClientRect().width) continue;
          probe.style.font = getComputedStyle(p).font;
          const ch = p.getBoundingClientRect().width / (probe.getBoundingClientRect().width / 10);
          if (ch > max.ch) max = { ch, text: p.textContent!.slice(0, 40) };
        }
        probe.remove();
        return max;
      });
      expect(widest.ch, widest.text).toBeLessThanOrEqual(72.5);
      for (const box of await page.locator('[data-slot=page-container]').evaluateAll(els => els.map(e => e.getBoundingClientRect().width)))
        expect(box).toBeLessThanOrEqual(1600);
      expect(await axe(page)).toEqual([]);
    });

// AC2: body text is weight 500 and the identifying cell of a list row is bold.
test('body text is weight 500 and a list row names its patient in bold', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page, reception, 'en');
  await page.route('**/v1/patients/search**', r => r.fulfill({ json: { matchedOn: 'Directory', page: 1, pageSize: 20, total: 1,
    items: [{ id: 'p-1', initials: 'MI', fullName: 'Meera Iyer (synthetic)', ageYears: 54, ageEstimated: false, sexAtBirth: 'Female', mrnTail: '0012', mobileTail: '0101', status: 'Complete' }] } }));
  await page.goto('/patients/directory');
  await expect(page.getByRole('rowheader')).toHaveCount(1);
  expect(await page.evaluate(() => getComputedStyle(document.body).fontWeight)).toBe('500');
  const row = page.getByRole('row').nth(1);
  expect(Number(await row.getByRole('rowheader').evaluate(e => getComputedStyle(e).fontWeight))).toBeGreaterThanOrEqual(700);
  expect(await row.getByRole('cell').first().evaluate(e => getComputedStyle(e).fontWeight)).toBe('500');
});

// AC4: the Register patient stepper at 375 px shows every step label in full.
test.describe('at 375 px', () => {
  test.use({ viewport: { width: 375, height: 812 } });
  for (const locale of ['en', 'ar'])
    test(`the ${locale} registration stepper clips no step label`, async ({ page }) => {
      await openScreen(page, { path: '/patients/new', as: reception }, locale);
      const steps = page.getByRole('list', { name: t(locale, 'registration.steps.label') }).getByRole('button');
      expect(await steps.count()).toBeGreaterThan(1);
      const clipped = await steps.evaluateAll(buttons => buttons.flatMap(b => {
        const box = b.getBoundingClientRect(), label = b.lastElementChild as HTMLElement, r = label.getBoundingClientRect();
        return label.scrollWidth > label.clientWidth + 1 || r.left < box.left - 1 || r.right > box.right + 1 || box.right > window.innerWidth || box.left < 0
          ? [label.textContent] : [];
      }));
      expect(clipped).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
    });
});
