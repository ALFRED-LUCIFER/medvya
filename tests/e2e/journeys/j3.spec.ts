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
  // MDV-P1-025 / MDV-P1-052: a walk-in is checked in and the desk reads the token from the success screen.
  1: async ({ page }) => {
    await page.route('**/v1/me/context', r => r.fulfill({ json: reception }));
    await page.route('**/v1/schedule?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', start: '2026-10-19T02:30:00Z', end: '2026-10-19T15:30:00Z',
      columns: [{ calendarId: 'd-1', calendarKind: 'Doctor', name: 'Dr. Arjun Rao (synthetic)', slotMinutes: 20, hours: [], slots: [], unavailable: [], appointments: [], visitLengths: [], capacity: 0 }] } }));
    await page.route('**/v1/schedule/patients/p-1?*', r => r.fulfill({ json: { id: 'p-1', fullName: 'Farhan Qureshi (synthetic)', mrn: '000101' } }));
    await page.route('**/v1/appointments/walk-in', r => r.fulfill({ json: { id: 'a-1', calendarId: 'd-1', start: farhan.start, end: farhan.end, walkIn: true, unscheduled: false, patientName: farhan.patientName } }));
    await page.route('**/v1/appointments/a-1/check-in', r => r.fulfill({ json: { ...farhan, room: null, token: 'A-07', walkIn: true } }));
    await page.goto('/front-desk/walk-in?patient=p-1');
    await expect(page.getByText('Patient: Farhan Qureshi (synthetic) · MRN 000101')).toBeVisible();
    await page.getByRole('button', { name: 'Check in now' }).click();
    await expect(page.getByRole('heading', { name: 'Farhan Qureshi (synthetic) is checked in' })).toBeVisible();
    await expect(page.getByText('Token A-07')).toBeVisible();
  },
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
