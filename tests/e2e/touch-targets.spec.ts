import { expect, test, type Page } from '@playwright/test';
import { admin, at, axe, board, persona, signIn } from './support/desk';

// MDV-P1-078 (AC4, AC6): on Today, settings and registration at 375, 768 and 1024 touch, no control's target is under 24 px either way,
// and every radio, checkbox and patient row is at least 44 px tall as a target (the ChoiceRow label; the stretched name link's row).
// The measuring follows docs/product-ux-audit/tools/states-measure.mjs. Mocked API, as in the other specs (CI has none).
const doctor = { ...persona('SoloDoctor', { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'Full', ClinicalNotes: 'Full' }),
  user: { id: 'd-1', displayName: 'Dr. Arjun Rao (synthetic)' } };

async function today(page: Page) {
  await page.route('**/v1/chart/allergy-flags', r => r.fulfill({ json: { items: board.cards.map(c => ({ patientId: c.patientId, state: 'NotRecorded', abnormalVitals: false })) } }));
  await page.route('**/v1/intake-escalations?*', r => r.fulfill({ json: [{ id: 'x-1', appointmentId: 'c2', patientId: 'p-2', reason: 'Fever (synthetic)', escalatedAt: at('10:20') }] }));
  await page.route('**/v1/notes/pending', r => r.fulfill({ json: { more: false, items: [] } }));
}
const queueRule = (page: Page) => page.route('**/v1/settings/queue-rule', r => r.fulfill({ json: { order: 'Arrival', bookedFirst: false } }));

const screens = [
  { name: 'Today', path: '/today', as: doctor, mock: today },
  { name: 'registration', path: '/patients/new', as: admin },
  { name: 'quick registration', path: '/patients/new?mode=quick', as: admin },
  { name: 'settings queue', path: '/settings?tab=queue', as: admin, mock: queueRule },
  { name: 'settings policy', path: '/settings?tab=policy', as: admin },
  { name: 'settings staff', path: '/settings?tab=staff', as: admin },
  { name: 'settings doctors', path: '/settings?tab=doctors', as: admin },
];

async function open(page: Page, s: (typeof screens)[number], locale: string) {
  await signIn(page, s.as, locale);
  await s.mock?.(page);
  await page.goto(s.path);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await page.waitForLoadState('networkidle');
  if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
}

/** Each visible control's target: a radio or checkbox is tapped through its label, a stretched link through the box its ::after covers. */
const undersized = (page: Page) => page.evaluate(() => {
  const out: string[] = [];
  const controls = 'button, a[href], input:not([type=hidden]), select, textarea, [role=combobox], [role=tab], [role=checkbox], [role=switch], [role=radio]';
  for (const el of document.querySelectorAll<HTMLElement>(controls)) {
    if (el.matches('a[href="#main"]') || getComputedStyle(el).visibility === 'hidden') continue; // the skip link shows only on focus
    if (el.tagName === 'A' && el.closest('p') && getComputedStyle(el, '::after').position !== 'absolute') continue; // a link in a sentence (WCAG 2.5.8)
    const choice = el.matches('input[type=checkbox], input[type=radio]');
    const target = choice ? el.closest('label') ?? el : getComputedStyle(el, '::after').position === 'absolute' ? el.offsetParent as HTMLElement ?? el : el;
    const r = target.getBoundingClientRect();
    if (r.width <= 1 || r.height <= 1) continue; // sr-only
    const name = `${el.tagName.toLowerCase()} "${(el.getAttribute('aria-label') ?? el.textContent ?? '').trim().slice(0, 30)}"`;
    if (r.width < 23.5 || r.height < 23.5) out.push(`${name} is ${Math.round(r.width)}x${Math.round(r.height)}`);
    else if ((choice || el.matches('#main a[href^="/patients/"]')) && r.height < 43.5) out.push(`${name} is ${Math.round(r.height)} px tall`);
  }
  return out;
});

test.describe('on a touch screen', () => {
  test.use({ hasTouch: true });
  for (const [w, h] of [[375, 812], [768, 1024], [1024, 768]] as const)
    for (const s of screens)
      test(`${s.name} at ${w}: no target under 24 px; radios, checkboxes and patient rows at least 44 px`, async ({ page }) => {
        await page.setViewportSize({ width: w, height: h });
        await open(page, s, 'en');
        expect(await undersized(page)).toEqual([]);
      });
});

for (const locale of ['en', 'ar'])
  for (const s of screens)
    test(`axe finds no violations on ${s.name} (${locale})`, async ({ page }) => {
      await page.setViewportSize({ width: 1024, height: 768 });
      await open(page, s, locale);
      expect(await axe(page)).toEqual([]);
    });
