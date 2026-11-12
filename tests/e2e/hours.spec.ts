import { expect, test, type Page } from '@playwright/test';

// MDV-P1-005: saving a bookable schedule says so (an announced status), and a failed save says that instead. Mocked API (CI has none).
const admin = {
  user: { id: 'u-lakshmi', displayName: 'K. Lakshmi (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic (synthetic)', facilityId: 'f-1', facilityName: 'Adyar (synthetic)', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', country: 'IN', role: 'PracticeAdmin', flags: [], capabilities: { StaffRolesIntegrations: 'Config' } },
};
const template = {
  calendarId: 'd-1', calendarKind: 'Doctor', name: 'Dr. Arjun Rao (synthetic)', slotMinutes: 20, breaks: [], visitLengths: [], appointmentsOutsideHours: [],
  hours: [{ weekday: 'Monday', start: '09:00:00', end: '13:00:00', room: null }],
};

async function saveSchedule(page: Page, saveStatus: number) {
  await page.route('**/v1/me/context', r => r.fulfill({ json: admin }));
  await page.route('**/v1/settings/locations', r => r.fulfill({ json: [{ id: 'f-1', name: 'Adyar (synthetic)', address: null, timeZone: 'Asia/Kolkata' }] }));
  await page.route('**/v1/settings/locations/*/hours', r => r.fulfill({ json: [] }));
  await page.route('**/v1/schedule/templates*', r => {
    if (r.request().method() !== 'PUT') return r.fulfill({ json: [template] });
    const body = r.request().postDataJSON() as { slotMinutes: number };
    return saveStatus === 200 ? r.fulfill({ json: { ...template, slotMinutes: body.slotMinutes } }) : r.fulfill({ status: saveStatus, json: {} });
  });
  await page.goto('/settings?tab=hours');
  await page.getByRole('combobox', { name: 'Slot length' }).click();
  await page.getByRole('option', { name: '30 min', exact: true }).click();
  await page.getByRole('button', { name: 'Save changes' }).last().click();
}

test('saving a bookable schedule says Saved in an announced status, and editing again clears it', async ({ page }) => {
  await saveSchedule(page, 200);
  await expect(page.getByRole('status').filter({ hasText: 'Saved.' })).toBeVisible();
  await page.getByRole('button', { name: 'Add session' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Saved.' })).toHaveCount(0);
});

test('a failed schedule save says so and shows no Saved', async ({ page }) => {
  await saveSchedule(page, 500);
  await expect(page.getByRole('alert').filter({ hasText: 'Nothing was changed' })).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: 'Saved.' })).toHaveCount(0);
});

async function openHours(page: Page, hours: () => unknown[]) {
  await page.route('**/v1/me/context', r => r.fulfill({ json: admin }));
  await page.route('**/v1/settings/locations', r => r.fulfill({ json: [{ id: 'f-1', name: 'Adyar (synthetic)', address: null, timeZone: 'Asia/Kolkata' }] }));
  await page.route('**/v1/settings/locations/*/hours', r => r.fulfill({ json: hours() }));
  await page.route('**/v1/schedule/templates*', r => r.fulfill({ json: [template] }));
  await page.goto('/settings?tab=hours');
  await expect(page.getByRole('heading', { name: 'Bookable schedules' })).toBeVisible();
}

// MDV audit UX-13: opening hours that contradict the doctors' bookable sessions are warned about, not silently kept.
test('a location closed all week while a doctor has sessions says so and names the sessions', async ({ page }) => {
  await openHours(page, () => []);
  const warning = page.getByRole('status').filter({ hasText: 'closed every day' });
  await expect(warning).toContainText('This location is closed every day, but doctors have bookable sessions. Set the opening hours.');
  await expect(warning).toContainText('Dr. Arjun Rao (synthetic) (Monday)');
});

test('sessions inside the opening hours raise no warning; a session past closing time does', async ({ page }) => {
  let closes = '17:00:00';
  await openHours(page, () => [{ weekday: 'Monday', opens: '09:00:00', closes }]);
  await expect(page.getByText('fall outside the opening hours')).toHaveCount(0);
  closes = '12:00:00';
  await page.reload();
  await expect(page.getByRole('status').filter({ hasText: 'fall outside the opening hours' })).toContainText('Dr. Arjun Rao (synthetic) (Monday)');
});
