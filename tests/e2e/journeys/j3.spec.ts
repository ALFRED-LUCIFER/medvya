import { expect } from '@playwright/test';
import { mockNurse } from '../support/nurse';
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
  // MDV-P2-007: the nurse opens the intake from the With nurse card, records vitals and hands off; the card leaves the nurse's queue.
  // shortcut: step 6 (the doctor sees it on Today) waits for the doctor's Today list, MDV-P2-002.
  5: async ({ page }) => {
    const { calls } = await mockNurse(page);
    await page.goto('/front-desk');
    await page.getByRole('article', { name: 'Meera Iyer (synthetic)' }).getByRole('link', { name: 'Open intake for Meera Iyer (synthetic)' }).click();
    await page.getByRole('textbox', { name: 'Pulse' }).fill('72');
    await page.getByRole('button', { name: 'Save vitals' }).click();
    await page.getByRole('button', { name: 'Confirm: no known allergies' }).click();
    await page.getByRole('button', { name: 'Hand off · Ready for doctor' }).click();
    await expect(page).toHaveURL(/\/nurse$/);
    await expect(page.getByRole('heading', { name: 'No one is with the nurse' })).toBeVisible();
    expect(calls.handoffs).toBe(1);
  },
  // MDV-P1-072: a desk at the organisation's second location finds a patient registered at the first one, and it opens as the same chart and MRN.
  8: async ({ page }) => {
    const secondLocation = { ...reception, active: { ...reception.active, facilityId: 'f-2' } };
    const meeraId = '5e1d0001-0004-4000-8000-000000000001';
    await page.route('**/v1/me/context', r => r.fulfill({ json: secondLocation }));
    await page.route('**/v1/patients/search**', r => r.fulfill({ json: { matchedOn: 'Name', items: [{ id: meeraId, initials: 'MI', ageYears: 54, ageEstimated: false, sexAtBirth: 'Female', mrnTail: '4482', status: 'Complete' }], page: 1, pageSize: 20, total: 1 } }));
    await page.route(`**/v1/patients/${meeraId}`, r => r.fulfill({ json: { id: meeraId, mrn: '004482', fullName: 'Meera Iyer (synthetic)', arabicName: null, dateOfBirth: '1972-03-12', dateOfBirthEstimated: false,
      ageYears: 54, sexAtBirth: 'Female', genderIdentity: null, preferredLanguage: 'Tamil', nationality: 'IN', mobile: '+919000000101', alternatePhone: null, email: null, status: 'Complete', registeredOn: '2026-01-01',
      address: null, identifiers: [], payer: null } }));
    await page.route(`**/v1/patients/${meeraId}/consents`, r => r.fulfill({ json: { policyVersion: '2026-11-draft', decisions: [] } }));
    await page.goto('/patients/directory');
    const search = page.getByRole('searchbox', { name: 'Name, MRN, phone or ID' });
    await search.fill('meera');
    await search.press('Enter');
    await page.getByRole('link', { name: /M I, 54 y, Female, MRN ending 4482/ }).click();
    await expect(page.getByRole('heading', { name: 'Meera Iyer (synthetic)', level: 1 })).toBeVisible();
  },
});
