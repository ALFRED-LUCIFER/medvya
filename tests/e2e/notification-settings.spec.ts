import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-006: Settings -> Notifications and Data. CI has no API: the endpoints are mocked in the shapes the server sends
// (INotifications.cs); the server side (cancelling queued messages, quiet hours, 403) is covered by NotificationsTests and
// PracticeSettingsTests on SQL Server. No journey row covers settings, so this is a spec of its own.
const admin = {
  user: { id: 'u-1', displayName: 'P. Admin (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic (synthetic)', facilityId: 'f-1', facilityName: 'Adyar (synthetic)', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', country: 'IN', role: 'PracticeAdmin', flags: [] as string[], capabilities: { RegisterBookPatients: 'Full', StaffRolesIntegrations: 'Config', AuditLog: 'Read' } },
};
const templates = (lang: string) => [
  { group: 'Confirmation', subject: lang === 'en' ? 'Appointment confirmed' : 'تأكيد الموعد', text: lang === 'en' ? 'Your appointment at {clinic} is confirmed for {date} at {time}.' : 'تم تأكيد موعدك في {clinic} بتاريخ {date} الساعة {time}.' },
  { group: 'Reminder', subject: 'Appointment reminder', text: 'Reminder: your appointment at {clinic} is on {date} at {time}.' },
  { group: 'Rescheduled', subject: 'Appointment moved', text: 'Your appointment at {clinic} has been moved to {date} at {time}.' },
  { group: 'Cancelled', subject: 'Appointment cancelled', text: 'Your appointment at {clinic} on {date} at {time} has been cancelled.' },
  { group: 'NeedsRebooking', subject: 'Please rebook your appointment', text: 'Your appointment at {clinic} on {date} at {time} cannot go ahead.' },
];

async function open(page: Page, tab: string, locale = 'en') {
  const saved = { puts: [] as { body: unknown; csrf: string | undefined }[] };
  let prefs = { confirmation: true, reminder24h: true, reminder2h: true, changes: true, email: true, sms: true, quietStart: '21:00:00', quietEnd: '08:00:00', senderName: null as string | null };
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: admin }));
  await page.route('**/v1/settings/notifications/templates?*', r => r.fulfill({ json: templates(locale) }));
  await page.route('**/v1/settings/notifications', r => {
    if (r.request().method() === 'PUT' && /iyer-clinic\.example/.test(r.request().postData() ?? ''))
      return r.fulfill({ status: 400, json: { title: 'One or more validation errors occurred.', errors: { senderName: ['link'] } } });
    if (r.request().method() === 'PUT') {
      prefs = r.request().postDataJSON() as typeof prefs;
      saved.puts.push({ body: prefs, csrf: r.request().headers()['x-medvya-csrf'] });
    }
    return r.fulfill({ json: prefs });
  });
  await page.goto(`/settings?tab=${tab}`);
  return saved;
}

test('the owner turns off a reminder and changes the quiet hours; the server gets the whole setting', async ({ page }) => {
  const saved = await open(page, 'notifications');
  const reminder = page.getByRole('switch', { name: /Reminder 24 hours before/ });
  await expect(reminder).toBeChecked();
  await reminder.click();
  await page.getByLabel('Quiet until').fill('09:30');
  await page.getByRole('button', { name: 'Save changes' }).click();
  await expect(page.getByText('Saved.')).toBeVisible();
  expect(saved.puts).toEqual([{ body: { confirmation: true, reminder24h: false, reminder2h: true, changes: true, email: true, sms: true, quietStart: '21:00:00', quietEnd: '09:30:00', senderName: null }, csrf: '1' }]);
  await expect(page.getByText('Turning a message off cancels the ones already waiting')).toBeVisible();
});

test('the email sender name is saved, and one with a web address is refused under the field (#213)', async ({ page }) => {
  const saved = await open(page, 'notifications');
  const name = page.getByLabel('Sender name in emails');
  await name.fill('Visit iyer-clinic.example');
  await page.getByRole('button', { name: 'Save changes' }).click();
  await expect(page.getByText('Remove the web address', { exact: false })).toBeVisible();
  await name.fill('Iyer Family Clinic');
  await page.getByRole('button', { name: 'Save changes' }).click();
  await expect(page.getByText('Saved.')).toBeVisible();
  expect(saved.puts.at(-1)!.body).toMatchObject({ senderName: 'Iyer Family Clinic' });
});

test('templates show placeholders only and WhatsApp is a disabled placeholder', async ({ page }) => {
  await open(page, 'notifications');
  const wording = page.getByRole('region', { name: 'Message wording' });
  await expect(wording).toContainText('Your appointment at {clinic} is confirmed for {date} at {time}.');
  await expect(page.getByRole('switch', { name: /WhatsApp/ })).toBeDisabled();
  expect(await wording.textContent()).not.toMatch(/Meera|Iyer|Dr\.|\d{2}:\d{2}/);
});

test('the Data tab shows country and residency read-only, retention, the export placeholder and a link to the access log', async ({ page }) => {
  await open(page, 'data');
  await expect(page.getByRole('region', { name: 'Country and data residency' })).toContainText('India');
  await expect(page.getByRole('region', { name: 'Country and data residency' })).toContainText('Data stays in India · Azure Central India');
  await expect(page.getByRole('region', { name: 'Retention' })).toContainText('Audio kept 30 days by default');
  await expect(page.getByRole('region', { name: 'Export your data' })).toContainText('Coming in a later phase.');
  await expect(page.getByRole('main').getByRole('textbox')).toHaveCount(0); // nothing to edit
  await page.getByRole('link', { name: 'Open the access log' }).click();
  await expect(page).toHaveURL(/tab=audit$/);
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  for (const tab of ['notifications', 'data'])
    test(`axe finds no violations on Settings -> ${tab} (${locale}); 44 px targets`, async ({ page }) => {
      await open(page, tab, locale);
      await expect(page.getByRole('main').getByRole('heading', { level: 2 }).first()).toBeVisible();
      if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
      // A switch is tapped through its label row (the same pattern as the hours toggles), so measure that row.
      for (const c of await page.getByRole('main').locator('button:not([role=switch]):visible, a:visible, input:not([aria-hidden=true]):visible, label:has([role=switch])').all())
        expect((await c.boundingBox())!.height, await c.evaluate(e => e.outerHTML.slice(0, 80))).toBeGreaterThanOrEqual(44);
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
          .map(v => `${v.id} (${v.nodes.length})`));
      expect(violations).toEqual([]);
    });
