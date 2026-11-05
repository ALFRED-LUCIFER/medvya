import { expect, test } from '@playwright/test';

// MDV-P1-048: a slow or flaky connection. The shared API client retries a GET after a 503 and a dropped connection,
// so the screen loads without the user pressing anything. CI has no API: the calls are mocked (synthetic data only).
const clinician = {
  user: { id: 'u-meera', displayName: 'Dr. M. Nair' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'HospitalDoctor' }],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'HospitalDoctor', flags: [],
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Read', ClinicalNotes: 'Sign', StaffRolesIntegrations: 'None' },
  },
};

for (const locale of ['en', 'ar']) {
  test(`a GET that fails twice still loads the merge queue (${locale})`, async ({ page }) => {
    let calls = 0;
    await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
    await page.route('**/v1/me/context', r => r.fulfill({ json: clinician }));
    await page.route('**/v1/patients/merge-requests', r => {
      calls++;
      if (calls === 1) return r.fulfill({ status: 503 });
      if (calls === 2) return r.abort('connectionreset');
      return r.fulfill({ json: { requests: [{ id: '5e1d0001-0048-4000-8000-000000000001', status: 'AwaitingApproval', survivorMrn: '000012', mergedMrn: '000041', decidedAt: '2026-11-02T04:00:00Z' }] } });
    });
    await page.goto('/patients/directory/merge');
    await expect(page.getByRole('link', { name: /000012/ })).toBeVisible({ timeout: 10_000 });
    expect(calls).toBe(3);
  });
}
