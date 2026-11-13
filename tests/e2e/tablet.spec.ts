import { devices, expect, test } from '@playwright/test';
import { at, axe, board, calendar, openScreen, problems, reception, screens, signIn, t } from './support/desk';

// MDV-P1-036: the reception screens on a desk tablet (768x1024 portrait, 1024x768 landscape), English and Arabic RTL.
// Every screen: no clipped controls (the page never scrolls sideways; only the board scrolls on its own), every control
// at least 44 px (WCAG 2.5.8 target), axe clean. Mocked API as in the other specs (CI has none).

// A desk tablet is a touch screen: it reports a coarse pointer, so a compact table is comfortable on it (MDV-P2-054, DESIGN.md Density).
test.describe('on a touch tablet', () => {
  test.use({ hasTouch: true });
  for (const [w, h] of [[768, 1024], [1024, 768]] as const)
    for (const locale of ['en', 'ar'])
      for (const s of screens)
        test(`${s.name} at ${w}x${h} (${locale}): nothing clipped, targets >= 44 px, axe clean`, async ({ page }) => {
          await page.setViewportSize({ width: w, height: h });
          await openScreen(page, s, locale);
          expect(await page.evaluate(() => matchMedia('(any-pointer: coarse)').matches)).toBe(true);
          expect(await problems(page)).toEqual([]);
          expect(await axe(page)).toEqual([]);
        });
});

// Before the first slot of the fixture day, so every slot is still bookable.
for (const [w, h] of [[768, 1024], [1024, 768]] as const)
  test(`the booking panel at ${w}x${h} is a drawer over the calendar, closed by Escape or its scrim`, async ({ page }) => {
    await page.clock.setFixedTime(new Date(at('08:00')));
    await page.route('**/v1/slot-holds', r => r.fulfill({ json: { id: 'h-1', facilityId: 'f-1', calendarId: 'd-1', visitType: 'FollowUp', start: at('09:20'), end: at('09:40'), expiresAt: at('08:02') } }));
    await page.route('**/v1/slot-holds/h-1', r => r.fulfill({ status: 204 }));
    await page.setViewportSize({ width: w, height: h });
    await signIn(page, reception, 'en');
    await page.goto('/schedule');
    // With nothing being booked, the hint sits above the calendar instead of covering it (checked for every screen above).
    const slot = page.getByRole('button', { name: 'Book Dr. d-1 (synthetic) at 09:20' });
    await slot.click();
    const panel = page.getByRole('complementary', { name: 'Book appointment' });
    await expect(panel.getByRole('button', { name: 'Cancel' })).toBeVisible();
    // The drawer scrolls on its own when taller than the screen, so the frame must be fully on screen and the last control reachable.
    await expect(panel.locator('../..')).toBeInViewport({ ratio: 1 }); // the drawer's frame (around its focus scope)
    const last = panel.getByRole('button').last();
    await last.scrollIntoViewIfNeeded();
    await expect(last).toBeInViewport({ ratio: 1 });
    expect(await problems(page, 'aside[aria-label="Book appointment"]')).toEqual([]);

    await panel.getByRole('textbox').first().focus();
    await page.keyboard.press('Escape');
    await expect(panel.getByRole('button', { name: 'Cancel' })).toBeHidden();
    expect(await problems(page)).toEqual([]);

    await slot.click();
    await expect(panel.getByRole('button', { name: 'Cancel' })).toBeVisible();
    await page.mouse.click(20, h - 20); // the scrim, beside the drawer
    await expect(panel.getByRole('button', { name: 'Cancel' })).toBeHidden();

    // Screen readers get a modal dialog; a typed reason is not lost to a stray Escape or scrim tap (MDV-P1-036).
    await slot.click();
    const dialog = page.getByRole('dialog', { name: 'Appointment panel' });
    await expect(dialog).toHaveAttribute('aria-modal', 'true');
    await dialog.getByLabel("Reason in the patient's words (optional)").fill('Synthetic: sugar high');
    const asked: string[] = [];
    let accept = false;
    page.on('dialog', d => { asked.push(d.message()); void (accept ? d.accept() : d.dismiss()); });
    await page.keyboard.press('Escape');
    await expect(dialog).toBeVisible(); // kept: the desk said no
    accept = true;
    await page.mouse.click(20, h - 20);
    await expect(dialog).toBeHidden();
    expect(asked).toHaveLength(2);
  });

test.describe('touch tablet', () => {
  test.use({ hasTouch: true, viewport: { width: 768, height: 1024 } });
  test('check-in is two taps on the board', async ({ page }) => {
    await signIn(page, reception, 'en');
    const checkedIn = page.waitForRequest('**/v1/appointments/c0/check-in');
    await page.route('**/v1/appointments/*/check-in', r => r.fulfill({ json: { ...board.cards[0], state: 'CheckedIn' } }));
    await page.goto('/front-desk');
    const card = page.getByRole('article', { name: 'Meera Iyer (synthetic) 0' });
    await card.getByRole('button', { name: 'Check in' }).tap();
    await card.getByRole('button', { name: 'Confirm arrival' }).tap();
    await checkedIn;
  });

  test('a quick registration is typed and saved by touch', async ({ page }) => {
    await signIn(page, reception, 'en');
    const saved = page.waitForRequest(r => r.url().endsWith('/v1/patients') && r.method() === 'POST');
    await page.route('**/v1/patients', r => r.fulfill({ json: { patientId: 'p-9', mrn: '001093', registeredAt: at('10:30'), status: 'Incomplete', warnings: [] } }));
    await page.goto('/patients/new?mode=quick');
    await page.getByLabel('Full name').tap();
    await page.keyboard.type('Kavya Iyer');
    await page.getByLabel('Mobile').fill('+91 90000 00202');
    await page.getByRole('textbox', { name: 'Date of birth (required)', exact: true }).fill('1990-02-03');
    await page.getByRole('radio', { name: 'Female', exact: true }).tap();
    await page.getByRole('button', { name: 'Register patient' }).tap();
    await expect(page.getByRole('heading', { name: 'Kavya Iyer is registered' })).toBeVisible();
    expect((await saved).postDataJSON()).toMatchObject({ fullName: 'Kavya Iyer', sexAtBirth: 'Female' });
  });
});

test('the board scrolls sideways on its own at 768 px, not the page', async ({ page }) => {
  await page.setViewportSize({ width: 768, height: 1024 });
  await signIn(page, reception, 'en');
  await page.goto('/front-desk');
  const scroller = page.getByRole('region', { name: 'Visits by status' });
  await expect(scroller).toBeVisible();
  const [scrolls, pageScrolls] = await scroller.evaluate(el => [el.scrollWidth > el.clientWidth, document.documentElement.scrollWidth > document.documentElement.clientWidth]);
  expect(scrolls).toBe(true);
  expect(pageScrolls).toBe(false);
});

// Real tablet profiles (viewport, scale, touch, mobile viewport), portrait and landscape: the desk's day on each, English and Arabic.
// shortcut: Chromium only; the iPad profiles' own browser (WebKit) is not installed in CI, add a webkit project when it is.
const shots = process.env.TABLET_SHOTS; // a folder: saves what each device showed, for a person to look at
for (const name of ['iPad (gen 7)', 'iPad Pro 11', 'Galaxy Tab S4'].flatMap(d => [d, `${d} landscape`]))
  for (const locale of ['en', 'ar'])
    test.describe(`${name} (${locale})`, () => {
      const { defaultBrowserType: _, ...device } = devices[name]!; // the browser comes from the project
      test.use(device);
      test('board check-in, booking a slot and a quick registration, by touch', async ({ page }) => {
        const shot = async (what: string) => { if (shots) await page.screenshot({ path: `${shots}/${name.replace(/\W+/g, '-')}-${locale}-${what}.png` }); };
        const bookings = await calendar(page, locale);
        await page.route('**/v1/appointments/*/check-in', r => r.fulfill({ json: { ...board.cards[0], state: 'CheckedIn' } }));
        await page.route('**/v1/patients', r => r.fulfill({ json: { patientId: 'p-9', mrn: '001093', registeredAt: at('10:30'), status: 'Incomplete', warnings: [] } }));

        await page.goto('/front-desk');
        const card = page.getByRole('article', { name: 'Meera Iyer (synthetic) 0' });
        await expect(card).toBeVisible();
        expect(await problems(page)).toEqual([]);
        await shot('board');
        const checkedIn = page.waitForRequest('**/v1/appointments/c0/check-in');
        await card.getByRole('button', { name: t(locale, 'board.action.checkIn') }).tap();
        await card.getByRole('button', { name: t(locale, 'board.confirm.arrived') }).tap();
        await checkedIn;

        await page.goto('/schedule?view=day');
        await page.getByRole('button', { name: t(locale, 'schedule.slot.book', { name: 'Dr. d-1 (synthetic)', time: '09:20' }) }).tap();
        const panel = page.getByRole('complementary', { name: t(locale, 'booking.title') });
        await panel.getByRole('searchbox', { name: t(locale, 'patients.search.label') }).fill('Iyer');
        await panel.getByRole('button', { name: t(locale, 'patients.search.submit') }).tap();
        await panel.getByRole('list').getByRole('button').tap();
        await expect(panel).toContainText('Meera Iyer (synthetic)');
        expect(await problems(page, 'aside')).toEqual([]);
        await shot('booking');
        await panel.getByRole('button', { name: t(locale, 'booking.confirm') }).tap();
        await expect.poll(() => bookings.length).toBe(1);

        await page.goto('/patients/new?mode=quick');
        await page.getByLabel(t(locale, 'registration.field.fullName')).fill('Kavya Iyer');
        await page.getByLabel(t(locale, 'registration.field.mobile')).fill('+91 90000 00202');
        await page.getByRole('textbox', { name: `${t(locale, 'registration.field.dateOfBirth')} ${t(locale, 'field.required')}`, exact: true }).fill('1990-02-03');
        await page.getByRole('radio', { name: t(locale, 'patients.sex.Female'), exact: true }).tap();
        expect(await problems(page)).toEqual([]);
        await shot('registration');
        await page.getByRole('button', { name: t(locale, 'registration.save') }).tap();
        await expect(page.getByRole('heading', { name: t(locale, 'registration.done.title', { name: 'Kavya Iyer' }) })).toBeVisible();
      });
    });
