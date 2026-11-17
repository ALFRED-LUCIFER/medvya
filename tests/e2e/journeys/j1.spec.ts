import { expect } from '@playwright/test';
import { mockBilling, mockInvoices } from '../support/billing';
import { completeWizard, mockOnboarding } from '../support/onboarding';
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
  // F-04: licence, clinic profile, hours and fees, invite the receptionist; the practice becomes the working context.
  1: async ({ page }) => {
    const api = await mockOnboarding(page);
    await page.goto('/onboarding?country=IN');
    await completeWizard(page);
    await expect.poll(() => api.chose).toBe('m-1');
    // #912: the doctor lands on My day (MDV-P2-002, named in MDV-P2-051), and My day is in the menu.
    await expect(page).toHaveURL(/\/today$/);
    await expect(page.getByRole('link', { name: 'My day' }).first()).toHaveAttribute('aria-current', 'page');
  },
  // F-11 (MDV-P4-002): an INR catalogue; the consultation is GST-exempt and priced, and a new service gets its clinic price as a version.
  2: async ({ page }) => {
    const api = await mockBilling(page);
    await page.goto('/settings?tab=services');
    await expect(page.getByRole('row', { name: /Consultation \(new visit\)/ })).toContainText('Exempt');
    await page.getByRole('button', { name: 'Add service' }).click();
    await page.getByRole('dialog', { name: 'New service' }).getByLabel('Name').fill('Dressing (synthetic)');
    await page.getByRole('button', { name: 'Save service' }).click();
    const editor = page.getByRole('dialog', { name: 'Dressing (synthetic)' });
    await editor.getByLabel('Price (INR)').fill('200');
    await editor.getByRole('button', { name: 'Save version' }).click();
    await expect(editor.getByText('Line total ₹200.00')).toBeVisible();
    expect(api.posts.at(-1)).toMatchObject({ body: { doctorId: null, amountMinor: 20_000 } });
  },
  // P-02: full registration with only the India minimum; ABHA left empty (optional, care never depends on it).
  3: async ({ page }) => {
    await page.route('**/v1/me/context', r => r.fulfill({ json: receptionist }));
    await page.route('**/v1/patients/duplicate-check', r => r.fulfill({ json: { candidates: [] } }));
    await page.route('**/v1/patients', r => r.fulfill({
      json: { patientId: '5e1d0001-0004-4000-8000-000000000501', mrn: '000001', registeredAt: '2026-10-25T04:46:00Z', status: 'Incomplete', warnings: [] },
    }));
    await page.goto('/patients/new');
    await page.getByLabel('Full name').fill('Meera Iyer');
    await page.getByRole('textbox', { name: 'Date of birth (required)', exact: true }).fill('1972-03-14');
    await page.getByRole('radio', { name: 'Female', exact: true }).check();
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByLabel('Mobile').fill('+91 90000 00501');
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByLabel('Emergency contact name').fill('Arun Iyer');
    await page.getByRole('combobox', { name: 'Relationship (required)', exact: true }).click();
    await page.getByRole('option', { name: 'Spouse', exact: true }).click();
    await page.getByLabel('Emergency contact phone').fill('+91 90000 00502');
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByRole('button', { name: 'Continue' }).click();
    await page.getByRole('switch', { name: 'Treatment' }).check();
    await page.getByRole('button', { name: 'Continue' }).click(); // consents: treatment only is required, ticked by the desk (#1365 F-040)
    await page.getByRole('button', { name: 'Register patient' }).click();
    await expect(page.getByRole('heading', { name: 'Meera Iyer is registered' })).toBeVisible();
  },
  // P-06 (MDV-P4-004): the board's Billing card opens the visit's bill (front-desk.spec checks the card's link) with the consultation suggested; the desk
  // confirms it, issues the bill and sends a Razorpay link; it is Paid only once the provider confirms. Mocked: the whole API, and Razorpay as the
  // "Check status" answer (support/billing.ts mockInvoices).
  8: async ({ page }) => {
    const api = await mockInvoices(page);
    await page.goto('/billing/visit/a5');
    await expect(page).toHaveURL(/\/billing\/inv-1$/);
    const line = page.getByRole('row', { name: /Consultation \(follow-up\)/ });
    await expect(line).toContainText('Suggested from the visit');
    await expect(page.getByRole('button', { name: 'Issue bill' })).toBeDisabled();
    await line.getByRole('button', { name: 'Confirm' }).click();
    await page.getByRole('button', { name: 'Issue bill' }).click();
    await expect(page.getByRole('heading', { name: 'Bill INV/26-27/00001' })).toBeVisible();
    await expect(page.getByText('Round off')).toBeVisible(); // always shown, even ₹0.00

    await page.getByRole('button', { name: 'Razorpay link for the balance' }).click();
    const pending = page.getByRole('status').filter({ hasText: 'Awaiting provider confirmation' });
    await expect(pending).toContainText('https://rzp.io/i/synthetic');
    await pending.getByRole('button', { name: 'Check status' }).click();
    await expect(page.getByText('Issued · awaiting payment')).toBeVisible(); // the provider has not confirmed: not paid
    api.provider.paid = true;
    await pending.getByRole('button', { name: 'Check status' }).click();
    await expect(page.getByText('Paid', { exact: true }).first()).toBeVisible();
    await expect(page.getByText('Awaiting provider confirmation')).toHaveCount(0);
    expect(api.posts.map(p => p.path)).toEqual(['line', 'issue', 'payment-link', 'check', 'check']);
  },
});
