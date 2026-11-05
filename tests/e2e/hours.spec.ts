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
