import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P1-017 merge review (P-03). CI has no API: /v1/me/context and /v1/patients/merge-requests* are mocked with the synthetic cast;
// the approvals, suppression, undo and audit rules are covered server-side by PatientMergeTests on SQL Server.
const clinician = {
  user: { id: 'u-meera', displayName: 'Dr. M. Nair' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'HospitalDoctor' }],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'HospitalDoctor', flags: [],
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Read', ClinicalNotes: 'Sign', StaffRolesIntegrations: 'None' },
  },
};
const requestId = '5e1d0001-0017-4000-8000-000000000001';
const survivorId = '5e1d0001-0017-4000-8000-0000000000a1';
const side = (id: string, mrn: string, name: string, visits: number) => ({
  patientId: id, fullName: name, mrn, dateOfBirth: '1971-03-09', sexAtBirth: 'Male', mobile: '+919000000301',
  identifiers: [{ scheme: 'Abha', lastFour: '0301' }], visits,
});
const review = (status: string, approvals = 0) => ({
  id: requestId, status, survivor: side(survivorId, '000012', 'Ravi Menon (synthetic)', 3), merged: side('5e1d0001-0017-4000-8000-0000000000a2', '000041', 'Ravindran Menon K (synthetic)', 1),
  approvalsRequired: 1, approvals, canUndo: status === 'Merged',
});

async function clinicWith(page: Page, locale = 'en') {
  let status = 'AwaitingApproval';
  const posts: string[] = [];
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: clinician }));
  await page.route('**/v1/patients/merge-requests', r => r.fulfill({ json: { requests: status === 'Merged' ? [] : [{ id: requestId, status, survivorMrn: '000012', mergedMrn: '000041', decidedAt: '2026-11-02T04:00:00Z' }] } }));
  await page.route(`**/v1/patients/merge-requests/${requestId}`, r => r.fulfill({ json: review(status, status === 'Merged' ? 1 : 0) }));
  await page.route(`**/v1/patients/merge-requests/${requestId}/*`, r => {
    const step = new URL(r.request().url()).pathname.split('/').at(-1)!;
    posts.push(`${r.request().method()} ${step} csrf=${r.request().headers()['x-medvya-csrf']}`);
    if (step === 'approve') status = 'Merged';
    return step === 'approve' ? r.fulfill({ json: { status } }) : r.fulfill({ status: 204 });
  });
  return posts;
}

test('a clinician compares the two records, approves and the merge shows as done', async ({ page }) => {
  const posts = await clinicWith(page);
  await page.goto('/patients/directory/merge');
  await expect(page.getByRole('heading', { name: 'Merge review', level: 1 })).toBeVisible();
  await page.getByRole('link', { name: /Record 000012 and record 000041/ }).click();
  await expect(page).toHaveURL(`/patients/directory/merge/${requestId}`);

  const table = page.getByRole('table', { name: 'Records side by side' });
  await expect(table.getByRole('columnheader', { name: 'Kept record' })).toBeVisible();
  await expect(table.getByText('Ravi Menon (synthetic)')).toBeVisible();
  await expect(table.getByText('Ravindran Menon K (synthetic)')).toBeVisible();
  await expect(table.getByText('••0301')).toHaveCount(2); // identifiers are masked to the last four
  await expect(page.getByText('Approvals: 0 of 1')).toBeVisible();
  // Awaiting approval: a clinician approves or says "not the same"; asking for approval is already done.
  await expect(page.getByRole('button', { name: 'Request clinician approval' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Undo merge' })).toHaveCount(0);

  await page.getByRole('button', { name: 'Approve merge' }).click();
  await expect(page.getByText('The merge is done.')).toBeVisible();
  expect(posts).toEqual(['POST approve csrf=1']);
  await expect(page.getByRole('button', { name: 'Undo merge' })).toHaveCount(0); // undo is an admin step (Staff, roles, integrations)
});

test('a refused step explains itself', async ({ page }) => {
  await clinicWith(page);
  await page.route(`**/v1/patients/merge-requests/${requestId}/approve`, r =>
    r.fulfill({ status: 400, json: { errors: [{ field: 'merge', code: 'same_approver' }] } }));
  await page.goto(`/patients/directory/merge/${requestId}`);
  await page.getByRole('button', { name: 'Approve merge' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'A second, different clinician must approve.' })).toBeVisible();
});

test('the record page lists its merge history', async ({ page }) => {
  await clinicWith(page);
  await page.route(`**/v1/patients/${survivorId}`, r => r.fulfill({ json: {
    id: survivorId, mrn: '000012', fullName: 'Ravi Menon (synthetic)', arabicName: null, dateOfBirth: '1971-03-09', dateOfBirthEstimated: false, ageYears: 55,
    sexAtBirth: 'Male', genderIdentity: null, preferredLanguage: null, nationality: 'IN', mobile: '+919000000301', alternatePhone: null, email: null,
    address: null, status: 'Complete', registeredOn: '2026-11-02', identifiers: [], payer: null, emergencyContact: null, guardian: null,
  } }));
  await page.route(`**/v1/patients/${survivorId}/merges`, r => r.fulfill({ json: { merges: [{ id: requestId, status: 'Merged', thisIsSurvivor: true, otherMrn: '000041', mergedAt: '2026-11-03T05:00:00Z', unmergedAt: null }] } }));
  await page.route(`**/v1/patients/${survivorId}/consents`, r => r.fulfill({ json: { policyVersion: '2026-11-draft', decisions: [] } }));
  await page.goto(`/patients/directory/${survivorId}`);
  await expect(page.getByRole('heading', { name: 'Merge history' })).toBeVisible();
  await expect(page.getByText(/Record 000041 was merged into this record on/)).toBeVisible();
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
}

for (const locale of ['en', 'ar'])
  for (const width of [1366, 375])
    test(`merge review is axe clean and mirrors in RTL (${locale}, ${width}px)`, async ({ page }) => {
      await clinicWith(page, locale);
      await page.setViewportSize({ width, height: 900 });
      await page.goto(`/patients/directory/merge/${requestId}`);
      await expect(page.getByRole('table')).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.dir)).toBe(locale === 'ar' ? 'rtl' : 'ltr');
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      for (const button of await page.getByRole('main').getByRole('button').all()) expect((await button.boundingBox())!.height).toBeGreaterThanOrEqual(44);
      expect(await axe(page)).toEqual([]);
    });

// MDV-P2-054 AC5: at 375 px each field is a stacked block, its name first and bold, then each record's value under that record's name,
// so a mobile number or MRN never breaks across lines and nothing scrolls sideways.
test.describe('at 375 px', () => {
  test.use({ viewport: { width: 375, height: 812 } });
  for (const [locale, mobile, kept, merged] of [['en', 'Mobile', 'Kept record', 'Record merged into it'], ['ar', 'الجوال', 'السجل المحتفظ به', 'السجل المدموج فيه']] as const)
    test(`the records are compared field by field, every value labelled with its record, and nothing scrolls sideways (${locale})`, async ({ page }) => {
      await clinicWith(page, locale);
      await page.goto(`/patients/directory/merge/${requestId}`);
      const table = page.getByRole('region', { name: locale === 'en' ? 'Records side by side' : 'السجلان جنبًا إلى جنب' }).filter({ has: page.getByRole('table') }).last();
      const row = table.getByRole('row', { name: new RegExp(mobile) });
      const name = row.getByRole('rowheader');
      await expect(name).toHaveText(mobile);
      expect(await name.evaluate(e => Number(getComputedStyle(e).fontWeight))).toBeGreaterThanOrEqual(700);
      for (const [record, cell] of [[kept, row.getByRole('cell').first()], [merged, row.getByRole('cell').last()]] as const) {
        await expect(cell.getByText(record, { exact: true })).toBeVisible();
        const value = cell.getByText('+919000000301', { exact: true });
        await value.scrollIntoViewIfNeeded();
        await expect(value).toBeInViewport();
        expect((await value.boundingBox())!.height).toBeLessThan(30); // one line, not broken mid-number
      }
      expect(await table.evaluate(e => e.scrollWidth - e.clientWidth)).toBeLessThanOrEqual(0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
      expect(await axe(page)).toEqual([]);
    });
});

// MDV-P1-017: "Request merge review" on a record. Pick the other record with front-desk search, ask, land on the new review.
const otherId = '5e1d0001-0017-4000-8000-0000000000a2';
async function recordWithRequestSection(page: Page, locale = 'en') {
  const posts: unknown[] = [];
  await clinicWith(page, locale);
  await page.route(`**/v1/patients/${survivorId}`, r => r.fulfill({ json: {
    id: survivorId, mrn: '000012', fullName: 'Ravi Menon (synthetic)', arabicName: null, dateOfBirth: '1971-03-09', dateOfBirthEstimated: false, ageYears: 55,
    sexAtBirth: 'Male', genderIdentity: null, preferredLanguage: null, nationality: 'IN', mobile: '+919000000301', alternatePhone: null, email: null,
    address: null, status: 'Complete', registeredOn: '2026-11-02', identifiers: [], payer: null, emergencyContact: null, guardian: null,
  } }));
  await page.route(`**/v1/patients/${survivorId}/merges`, r => r.fulfill({ json: { merges: [] } }));
  await page.route(`**/v1/patients/${survivorId}/consents`, r => r.fulfill({ json: { policyVersion: '2026-11-draft', decisions: [] } }));
  await page.route('**/v1/patients/search**', r => r.fulfill({ json: { matchedOn: 'Directory', page: 1, pageSize: 20, total: 1,
    items: [{ id: otherId, initials: 'RK', fullName: 'Ravi Kumar (synthetic)', ageYears: 55, ageEstimated: false, sexAtBirth: 'Male', mrnTail: '0041', mobileTail: '0041', status: 'Complete' }] } }));
  await page.route('**/v1/patients/merge-requests', r => {
    if (r.request().method() !== 'POST') return r.fulfill({ json: { requests: [] } });
    posts.push(r.request().postDataJSON());
    return r.fulfill({ json: { id: requestId } });
  });
  await page.goto(`/patients/directory/${survivorId}`);
  return posts;
}

test('a record asks for a merge review of another record and opens that review', async ({ page }) => {
  const posts = await recordWithRequestSection(page);
  const section = page.getByRole('region', { name: 'Possible duplicate' });
  const ask = section.getByRole('button', { name: 'Request merge review' });
  await expect(ask).toBeDisabled(); // nothing chosen yet
  await section.getByRole('searchbox').fill('Menon');
  await section.getByRole('button', { name: 'Search' }).click();
  await section.getByRole('listitem').getByRole('button').click();
  await ask.click();
  await expect(page).toHaveURL(`/patients/directory/merge/${requestId}`);
  expect(posts).toEqual([{ patientId: survivorId, otherPatientId: otherId }]);
});

test('a refused request names the reason and stays on the record', async ({ page }) => {
  await recordWithRequestSection(page);
  await page.route('**/v1/patients/merge-requests', r => r.request().method() === 'POST'
    ? r.fulfill({ status: 400, json: { errors: [{ field: 'merge', code: 'pair_decided' }] } }) : r.fulfill({ json: { requests: [] } }));
  const section = page.getByRole('region', { name: 'Possible duplicate' });
  await section.getByRole('searchbox').fill('Menon');
  await section.getByRole('button', { name: 'Search' }).click();
  await section.getByRole('listitem').getByRole('button').click();
  await section.getByRole('button', { name: 'Request merge review' }).click();
  await expect(section.getByRole('alert')).toHaveText('These two records already have a decision.');
  await expect(page).toHaveURL(`/patients/directory/${survivorId}`);
});

for (const locale of ['en', 'ar'])
  test(`the request-review section is axe clean (${locale})`, async ({ page }) => {
    await recordWithRequestSection(page, locale);
    await expect(page.getByRole('region', { name: locale === 'ar' ? 'تكرار محتمل' : 'Possible duplicate' })).toBeVisible();
    expect(await axe(page)).toEqual([]);
  });
