import { expect } from '@playwright/test';
import { journey } from './storyboard';

// Storyboard: docs/02-design/journeys.md. Implement steps as their screens ship.
// CI has no API: the session and patient endpoints are mocked with the synthetic cast.
const receptionist = {
  user: { id: 'u-joseph', displayName: 'A. Joseph' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'Receptionist' }],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Receptionist', flags: [], country: 'IN',
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', InvoicesPayments: 'Full', Patient360Chart: 'None', ClinicalNotes: 'None' },
  },
};

journey('J1', {
  // P-02: full registration with only the India minimum; ABHA left empty (optional, care never depends on it).
  3: async ({ page }) => {
    await page.route('**/v1/me/context', r => r.fulfill({ json: receptionist }));
    await page.route('**/v1/patients/duplicate-check', r => r.fulfill({ json: { candidates: [] } }));
    await page.route('**/v1/patients', r => r.fulfill({
      json: { patientId: '5e1d0001-0004-4000-8000-000000000501', mrn: '000001', registeredAt: '2026-10-25T04:46:00Z', status: 'Incomplete', warnings: [] },
    }));
    await page.goto('/patients/new');
    await page.getByLabel('Full name').fill('Meera Iyer');
    await page.getByRole('textbox', { name: 'Date of birth', exact: true }).fill('1972-03-14');
    await page.getByRole('radio', { name: 'Female', exact: true }).check();
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByLabel('Mobile').fill('+91 90000 00501');
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByLabel('Emergency contact name').fill('Arun Iyer');
    await page.getByRole('combobox', { name: 'Relationship', exact: true }).click();
    await page.getByRole('option', { name: 'Spouse', exact: true }).click();
    await page.getByLabel('Emergency contact phone').fill('+91 90000 00502');
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByRole('button', { name: 'Register patient' }).click();
    await expect(page.getByRole('heading', { name: 'Meera Iyer is registered' })).toBeVisible();
  },
});
