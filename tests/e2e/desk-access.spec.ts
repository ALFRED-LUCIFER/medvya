import { expect, test, type Locator, type Page } from '@playwright/test';
import { at, axe, board, calendar, meera, openScreen, problems, reception, screens, signIn } from './support/desk';

// MDV-P1-036 and AGENTS.md §4: the desk screens by keyboard alone and at 200% zoom, English and Arabic. Mocked API (support/desk.ts).

/** Presses Tab until `target` has focus; fails after `max` presses, so an unreachable control is a failure, not a hang. */
async function tabTo(page: Page, target: Locator, max = 60) {
  await expect(target).toBeEnabled(); // a disabled control is not a tab stop yet
  for (let i = 0; i < max; i++) {
    if (await target.evaluate(el => el === document.activeElement)) return;
    await page.keyboard.press('Tab');
  }
  await expect(target).toBeFocused();
}

/**
 * Tabs through the whole page once. Every stop must show a focus ring and sit uncovered on screen once focused; Tab must always
 * move on (no trap) until focus leaves the page or comes back round; and no positive tabindex reorders it away from reading order.
 */
async function tabWalk(page: Page) {
  const out: string[] = [];
  if (await page.locator('[tabindex]:not([tabindex="0"]):not([tabindex="-1"])').count()) out.push('a positive tabindex reorders the page');
  let first = '', previous = '', same = 0;
  for (let i = 0; i < 250; i++) {
    await page.keyboard.press('Tab');
    const stop = await page.evaluate(() => {
      const focused = document.activeElement as HTMLElement | null;
      if (!focused || focused === document.body) return null;
      // A visually hidden input (the logo picker) shows its focus on the label that stands in for it.
      const tiny = focused.getBoundingClientRect().width <= 1 && focused.id;
      const el = (tiny && [...document.querySelectorAll<HTMLElement>(`label[for="${focused.id}"]`)].at(-1)) || focused; // after the field's own label
      const s = getComputedStyle(el);
      const ring = (s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) > 0) || s.boxShadow !== 'none';
      const r = el.getBoundingClientRect();
      const top = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
      const seen = r.width > 0 && r.height > 0 && r.right > 0 && r.left < innerWidth && r.bottom > 0 && r.top < innerHeight;
      const uncovered = !top || el.contains(top) || top.contains(el) || !!el.closest('label')?.contains(top) || !!top.closest('header, nav');
      const path: string[] = [];
      for (let n: Element | null = el; n && n !== document.body; n = n.parentElement) path.unshift(`${n.tagName}${[...(n.parentElement?.children ?? [])].indexOf(n)}`);
      return { id: path.join('>'), name: `${el.tagName.toLowerCase()} "${(el.getAttribute('aria-label') ?? el.textContent ?? '').trim().slice(0, 30)}"`, ring, seen, uncovered, skip: el.matches('a[href="#main"]') };
    });
    if (!stop || stop.id === first) return out; // left the page, or back round to the first stop
    // A date or time input takes one Tab per part (day, month, year), so only a stop that holds focus longer is a trap.
    same = stop.id === previous ? same + 1 : 0;
    if (same > 3) { out.push(`${stop.name} traps the focus`); return out; }
    if (!first) first = stop.id;
    previous = stop.id;
    if (same) continue;
    if (!stop.ring) out.push(`${stop.name} has no visible focus ring`);
    if (!stop.skip && !stop.seen) out.push(`${stop.name} is focused off screen`);
    if (!stop.skip && stop.seen && !stop.uncovered) out.push(`${stop.name} is focused under something else`);
  }
  out.push('more than 250 tab stops');
  return out;
}

for (const locale of ['en', 'ar'])
  for (const s of screens)
    test(`${s.name} by keyboard (${locale}): focus visible, uncovered, never trapped`, async ({ page }) => {
      await page.setViewportSize({ width: 1024, height: 768 });
      await openScreen(page, s, locale);
      expect(await tabWalk(page)).toEqual([]);
    });

// WCAG 1.4.10: 200% zoom on a 1280x800 screen leaves 640x400 CSS px, which the page must fit without sideways scrolling.
for (const locale of ['en', 'ar'])
  for (const s of screens)
    test(`${s.name} at 200% zoom (${locale}): reflows, nothing clipped, axe clean`, async ({ page }) => {
      await page.setViewportSize({ width: 640, height: 400 });
      await openScreen(page, s, locale);
      expect(await problems(page)).toEqual([]);
      expect(await axe(page)).toEqual([]);
    });

test.describe('the booking drawer on a tablet', () => {
  test.use({ viewport: { width: 768, height: 1024 } });

  test('takes the focus, keeps Tab inside, and gives it back to the slot on Escape, scrim or Cancel', async ({ page }) => {
    await calendar(page);
    const slot = page.getByRole('button', { name: 'Book Dr. d-1 (synthetic) at 09:20' });
    const panel = page.getByRole('complementary', { name: 'Book appointment' });
    const inside = () => panel.evaluate(el => el.contains(document.activeElement));
    const close = { Escape: () => page.keyboard.press('Escape'), scrim: () => page.mouse.click(20, 1000), Cancel: () => panel.getByRole('button', { name: 'Cancel' }).click() };
    for (const [how, go] of Object.entries(close)) {
      await tabTo(page, slot, 120);
      await page.keyboard.press('Enter');
      await expect(panel.getByRole('button', { name: 'Cancel' })).toBeVisible();
      await expect.poll(inside, { message: `focus moves into the drawer (${how})` }).toBe(true);
      for (let i = 0; i < 25; i++) { await page.keyboard.press(i % 5 === 4 ? 'Shift+Tab' : 'Tab'); expect(await inside(), `Tab ${i} stays in the drawer`).toBe(true); }
      await go();
      await expect(panel.getByRole('button', { name: 'Cancel' })).toBeHidden();
      await expect(slot, `focus returns to the slot after ${how}`).toBeFocused();
    }
  });

  test('a slot is booked by keyboard alone; Escape closes a menu without closing the drawer', async ({ page }) => {
    const bookings = await calendar(page);
    const panel = page.getByRole('complementary', { name: 'Book appointment' });
    await tabTo(page, page.getByRole('button', { name: 'Book Dr. d-1 (synthetic) at 09:20' }), 120);
    await page.keyboard.press('Enter');
    const visitType = panel.getByRole('combobox', { name: 'Visit type' });
    await tabTo(page, visitType);
    await page.keyboard.press('Enter');
    await expect(page.getByRole('listbox')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('listbox')).toBeHidden();
    await expect(visitType).toBeFocused();
    await expect(panel.getByRole('button', { name: 'Cancel' })).toBeVisible();

    await tabTo(page, panel.getByRole('searchbox', { name: 'Name, MRN, phone or ID' }));
    await page.keyboard.type('Iyer');
    await page.keyboard.press('Enter');
    const row = panel.getByRole('button', { name: /M I, 54 y, Female, MRN ending 0012/ });
    await tabTo(page, row);
    await page.keyboard.press('Enter');
    await expect(panel).toContainText('Meera Iyer (synthetic) · MRN 000012');
    await tabTo(page, panel.getByRole('button', { name: 'Confirm booking' }));
    await page.keyboard.press('Enter');
    await expect.poll(() => bookings.length).toBe(1);
    expect(bookings[0]).toMatchObject({ holdId: 'h-1', patientId: meera });
  });
});

test('check-in by keyboard on the board', async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 768 });
  await signIn(page, reception, 'en');
  const checkedIn = page.waitForRequest('**/v1/appointments/c0/check-in');
  await page.route('**/v1/appointments/*/check-in', r => r.fulfill({ json: { ...board.cards[0], state: 'CheckedIn' } }));
  await page.goto('/front-desk');
  const card = page.getByRole('article', { name: 'Meera Iyer (synthetic) 0' });
  await tabTo(page, card.getByRole('button', { name: 'Check in' }));
  await page.keyboard.press('Enter');
  await tabTo(page, card.getByRole('button', { name: 'Confirm arrival' }), 5);
  await page.keyboard.press('Enter');
  await checkedIn;
});

test('a quick registration by keyboard alone', async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 768 });
  await signIn(page, reception, 'en');
  const saved = page.waitForRequest(r => r.url().endsWith('/v1/patients') && r.method() === 'POST');
  await page.route('**/v1/patients', r => r.fulfill({ json: { patientId: 'p-9', mrn: '001093', registeredAt: at('10:30'), status: 'Incomplete', warnings: [] } }));
  await page.goto('/patients/new?mode=quick');
  await tabTo(page, page.getByLabel('Full name'));
  await page.keyboard.type('Kavya Iyer');
  await tabTo(page, page.getByLabel('Mobile'));
  await page.keyboard.type('+91 90000 00202');
  await tabTo(page, page.getByRole('textbox', { name: 'Date of birth', exact: true }));
  await page.keyboard.type('1990-02-03');
  // A radio group is one tab stop; the arrow keys choose within it.
  const sex = page.getByRole('radio');
  await tabTo(page, sex.first());
  while (!(await page.getByRole('radio', { name: 'Female', exact: true }).isChecked())) await page.keyboard.press('ArrowDown');
  await tabTo(page, page.getByRole('button', { name: 'Register patient' }));
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Kavya Iyer is registered' })).toBeVisible();
  expect((await saved).postDataJSON()).toMatchObject({ fullName: 'Kavya Iyer', sexAtBirth: 'Female' });
});
