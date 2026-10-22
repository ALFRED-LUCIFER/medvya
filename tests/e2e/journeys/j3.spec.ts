import { expect } from '@playwright/test';
import { journey } from './storyboard';

// Storyboard: docs/02-design/journeys.md. Implement steps as their screens ship.
// The API is mocked with the synthetic cast in the shapes the server sends (IFrontDeskService.cs).
const reception = {
  user: { id: 'u-r', displayName: 'A. Joseph' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'Receptionist' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Receptionist', flags: [], capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full' } },
};
const farhan = {
  id: 'a-1', reference: 'APT-0A1B2C3D', patientId: 'p-1', patientName: 'Farhan Qureshi (synthetic)', dateOfBirth: '1988-05-02', registrationIncomplete: false,
  calendarId: 'd-1', calendarName: 'Dr. Arjun Rao (synthetic)', visitType: 'FollowUp', reasonCategory: 'General',
  start: '2026-10-19T04:50:00Z', end: '2026-10-19T05:10:00Z', state: 'CheckedIn', since: '2026-10-19T04:50:00Z',
};

journey('J3', {
  // MDV-P1-028: reception sends a checked-in patient to the nurse; the card moves to "With nurse".
  4: async ({ page }) => {
    await page.route('**/v1/me/context', r => r.fulfill({ json: reception }));
    await page.route('**/v1/front-desk/board?*', r =>
      r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: '2026-10-19T05:02:00Z', cards: [farhan] } }));
    await page.route('**/v1/appointments/a-1/status', r => {
      Object.assign(farhan, { state: (r.request().postDataJSON() as { state: string }).state });
      return r.fulfill({ json: farhan });
    });
    await page.goto('/front-desk');
    await page.getByRole('article', { name: 'Farhan Qureshi (synthetic)' }).getByRole('button', { name: 'Send to nurse' }).click();
    await expect(page.getByRole('region', { name: /^With nurse/ }).getByRole('article', { name: 'Farhan Qureshi (synthetic)' })).toBeVisible();
  },
});
