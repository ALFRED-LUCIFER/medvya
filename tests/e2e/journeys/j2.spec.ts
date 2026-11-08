import { expect } from '@playwright/test';
import { journey } from './storyboard';

// Storyboard: docs/02-design/journeys.md. Implement steps as their screens ship.
// CI has no API: the session, board and encounter endpoints are mocked with the synthetic cast in the shapes the server sends.
const doctor = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [], country: 'IN',
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'Full', ClinicalNotes: 'Sign' } },
};
const at = '2026-10-19T04:30:00Z'; // 10:00 IST

journey('J2', {
  // D-01: the next patient is Meera, ready for the doctor; starting the consultation binds it to her visit and shows who it is for, in Room 3.
  // The doctor's own Today (MDV-P2-002): the next patient hero starts the consultation.
  1: async ({ page }) => {
    await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
    await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: at, cards: [{
      id: 'a1', reference: 'APT-A1', patientId: 'p-meera', patientName: 'Meera Iyer', dateOfBirth: '1972-03-14', registrationIncomplete: false,
      calendarId: 'u-rao', calendarName: 'Dr. Arjun Rao', visitType: 'FollowUp', reasonCategory: 'ChronicReview', start: at, end: at, state: 'ReadyForDoctor', since: at,
    }] } }));
    const encounter = { id: 'e-1', appointmentId: 'a1', patientId: 'p-meera', facilityId: 'f-1', leadClinicianId: 'u-rao', status: 'Active', authorityMode: 'Medvya', startedAt: at, endedAt: null, room: 'Room 3' };
    await page.route('**/v1/encounters', r => r.fulfill({ json: encounter }));
    await page.route('**/v1/encounters/e-1', r => r.fulfill({ json: { encounter, timeZone: 'Asia/Kolkata', treatmentConsent: true,
      patient: { id: 'p-meera', fullName: 'Meera Iyer', mrn: '000001', dateOfBirth: '1972-03-14', ageYears: 54, sexAtBirth: 'Female' } } }));
    await page.goto('/today');
    await expect(page.getByRole('region', { name: 'Next patient' })).toContainText('Meera Iyer');
    await page.getByRole('region', { name: 'Next patient' }).getByRole('button', { name: 'Start consultation with Meera Iyer' }).click();
    const banner = page.getByRole('region', { name: 'Patient identity' });
    await expect(banner.getByRole('heading', { name: 'Meera Iyer' })).toBeVisible();
    await expect(banner).toContainText('Room 3');
  },
});
