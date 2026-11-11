import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';
import { mockOrders } from './support/orders';

// MDV-P2-019 (E-09): the doctor refers a patient from the consultation, chooses what the letter carries, sends it by a channel (an emailed letter
// needs the patient's consent), moves it on with a document as evidence, and files a received letter. CI has no API: endpoints are mocked in the
// shapes the server sends. Consent at the moment of sending, the allowed moves, the audit rows and the letter's sections are proven on PostgreSQL
// in ReferralTests and the matrix tests.
type Step = { state: string; at: string; documentId: string | null; note: string | null };
type Referral = { id: string; patientId: string; direction: string; provider: string; urgency: string; reason: string | null; sections: string[]; state: string; channel: string | null;
  sentAt: string | null; open: boolean; letterDocumentId: string | null; history: Step[] };

async function open(page: Page, { locale = 'en', consent = true } = {}) {
  await mockOrders(page, { locale });
  const referrals: Referral[] = [];
  const calls = { created: [] as unknown[], sent: [] as unknown[], status: [] as unknown[], filed: [] as unknown[] };
  const step = (state: string, documentId: string | null = null, note: string | null = null): Step => ({ state, at: '2026-10-19T04:50:00Z', documentId, note });
  await page.route('**/v1/patients/p-1/referrals', r => r.fulfill({ json: referrals }));
  await page.route('**/v1/patients/p-1/documents', r => r.fulfill({ json: { patient: { fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12' }, canOpen: true, timeZone: 'Asia/Kolkata',
    documents: [{ id: 'd-1', type: 'LabReport', documentDate: '2026-10-18', source: 'Imported', scanStatus: 'Clean', contentType: 'application/pdf', sizeBytes: 100, uploadedBy: null, uploadedAt: '2026-10-18T05:00:00Z' }] } }));
  await page.route('**/v1/encounters/e-1/referrals', r => {
    const body = r.request().postDataJSON() as { provider: string; urgency: string; reason: string; sections: string[] };
    calls.created.push(body);
    const referral: Referral = { id: `r-${referrals.length + 1}`, patientId: 'p-1', direction: 'Out', ...body, state: 'Draft', channel: null, sentAt: null, open: true, letterDocumentId: null, history: [step('Draft')] };
    referrals.unshift(referral);
    return r.fulfill({ status: 201, json: referral });
  });
  await page.route('**/v1/referrals/in', r => {
    const body = r.request().postDataJSON() as { provider: string; reason: string | null; documentId: string };
    calls.filed.push(body);
    const referral: Referral = { id: `r-${referrals.length + 1}`, patientId: 'p-1', direction: 'In', provider: body.provider, urgency: 'Routine', reason: body.reason, sections: [], state: 'Received', channel: null, sentAt: null,
      open: true, letterDocumentId: body.documentId, history: [step('Received', body.documentId)] };
    referrals.unshift(referral);
    return r.fulfill({ status: 201, json: referral });
  });
  await page.route('**/v1/referrals/*/send', r => {
    const body = r.request().postDataJSON() as { channel: string };
    calls.sent.push(body);
    if (body.channel === 'Email' && !consent) return r.fulfill({ status: 409, json: { code: 'consent_required' } });
    const referral = referrals.find(x => x.id === new URL(r.request().url()).pathname.split('/')[3])!;
    Object.assign(referral, { state: 'Sent', channel: body.channel, sentAt: '2026-10-19T04:55:00Z', history: [...referral.history, step('Sent')] });
    return r.fulfill({ json: referral });
  });
  await page.route('**/v1/referrals/*/status', r => {
    const body = r.request().postDataJSON() as { state: string; documentId: string | null; note: string | null };
    calls.status.push(body);
    const referral = referrals.find(x => x.id === new URL(r.request().url()).pathname.split('/')[3])!;
    Object.assign(referral, { state: body.state, open: body.state !== 'Closed', history: [...referral.history, step(body.state, body.documentId, body.note)] });
    return r.fulfill({ json: referral });
  });
  await page.route('**/v1/referrals/*/letter', r => r.fulfill({ json: {
    patient: { fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', sexAtBirth: 'Female' }, facilityName: 'Iyer Family Clinic · Adyar', doctorName: 'Dr. Arjun Rao (synthetic)',
    provider: 'Dr. S. Menon, Endocrinology (synthetic)', urgency: 'Routine', reason: 'Uncontrolled HbA1c (synthetic)', at: '2026-10-19T04:55:00Z',
    sections: [{ section: 'Medicines', lines: [{ text: 'Metformin 500 mg', source: 'ClinicianVerified' }] }] } }));
  await page.route('**/v1/referrals/*/letter/pdf', r => r.fulfill({ status: 200, contentType: 'application/pdf', body: '%PDF-1.7 synthetic' }));
  await page.route('**/v1/patients/p-1/chart', r => r.fulfill({ json: { patient: { id: 'p-1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female' },
    treatmentConsent: true, timeZone: 'Asia/Kolkata', chart: { allergyState: 'NotRecorded', allergies: [], problems: [], medicines: [], lastVitals: [], trends: [], recentEncounters: [], openEncounterId: null, lastLabs: [], bmi: null } } }));
  return { referrals, calls };
}

test('the doctor refers the patient, sends the letter and moves it on with a document as evidence', async ({ page }) => {
  const { calls } = await open(page);
  await page.goto('/consult/e-1');
  await expect(page.getByText('No referrals yet.')).toBeVisible();
  await page.getByLabel('Doctor or facility').fill('Dr. S. Menon, Endocrinology (synthetic)');
  await page.getByLabel('Reason for the referral').first().fill('Uncontrolled HbA1c (synthetic)');
  await page.getByRole('checkbox', { name: 'Current medicines' }).check();
  await page.getByRole('button', { name: 'Save the referral' }).click();
  expect(calls.created).toEqual([{ provider: 'Dr. S. Menon, Endocrinology (synthetic)', urgency: 'Routine', reason: 'Uncontrolled HbA1c (synthetic)', sections: ['Medicines'] }]);
  const row = page.getByRole('row', { name: /Dr\. S\. Menon/ });
  await expect(row).toContainText('Draft');

  await page.getByRole('button', { name: 'Mark as sent' }).click();
  expect(calls.sent).toEqual([{ channel: 'Print' }]);
  await expect(row).toContainText('Sent');
  await expect(row).toContainText('Printed letter');

  await page.getByRole('combobox', { name: 'Move to' }).click();
  await page.getByRole('option', { name: 'Report received' }).click();
  await page.getByRole('combobox', { name: 'Evidence (optional)' }).click();
  await page.getByRole('option', { name: 'Lab report, 2026-10-18' }).click();
  await page.getByLabel('Note (optional)').fill('Report came by post');
  await page.getByRole('button', { name: 'Update status' }).click();
  expect(calls.status).toEqual([{ state: 'ReportReceived', documentId: 'd-1', note: 'Report came by post' }]);
  await expect(row).toContainText('Report received');
  await expect(page.getByText('1 step with a document as evidence')).toBeVisible();
});

test('an emailed letter without the patient\'s consent is refused in words and stays a draft', async ({ page }) => {
  await open(page, { consent: false });
  await page.goto('/consult/e-1');
  await page.getByLabel('Doctor or facility').fill('Dr. R. Kumar (synthetic)');
  await page.getByLabel('Reason for the referral').first().fill('Review (synthetic)');
  await page.getByRole('button', { name: 'Save the referral' }).click();
  await page.getByRole('combobox', { name: 'How the letter is given' }).click();
  await page.getByRole('option', { name: 'Email' }).click();
  await page.getByRole('button', { name: 'Mark as sent' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'The patient has not agreed to share their information.' })).toBeVisible();
  await expect(page.getByRole('row', { name: /Dr\. R\. Kumar/ })).toContainText('Draft');
});

test('a received letter is filed with its document and the letter page prints only what was chosen', async ({ page }) => {
  const { calls } = await open(page);
  await page.goto('/consult/e-1');
  await page.getByLabel('Referred by').fill('Dr. A. Rahman, Cardiology (synthetic)');
  await page.getByRole('combobox', { name: 'The letter' }).click();
  await page.getByRole('option', { name: 'Lab report, 2026-10-18' }).click();
  await page.getByRole('button', { name: 'File the letter' }).click();
  expect(calls.filed).toEqual([{ patientId: 'p-1', provider: 'Dr. A. Rahman, Cardiology (synthetic)', reason: null, documentId: 'd-1' }]);
  await expect(page.getByRole('row', { name: /Dr\. A\. Rahman/ })).toContainText('Received');

  await page.goto('/consult/e-1/referrals/r-1/letter');
  const letter = page.getByRole('article');
  await expect(letter).toContainText('Dr. S. Menon, Endocrinology (synthetic)');
  await expect(letter).toContainText('Metformin 500 mg');
  await expect(letter).toContainText('[Clinician verified]'); // each fact says where it came from
  await expect(letter).not.toContainText('Allergies'); // not selected: not in the letter
  await expect(page.getByRole('button', { name: 'Print or save as PDF' })).toBeVisible();
  const download = page.waitForEvent('download'); // the server's copy with its QR check (MDV-P2-019)
  await page.getByRole('button', { name: 'Download PDF' }).click();
  expect((await download).suggestedFilename()).toBe('referral-letter.pdf');
});

test('Patient 360 shows the open referrals', async ({ page }) => {
  const { referrals } = await open(page);
  referrals.push({ id: 'r-9', patientId: 'p-1', direction: 'Out', provider: 'Dr. S. Menon (synthetic)', urgency: 'Urgent', reason: 'x', sections: [], state: 'Sent', channel: 'Print', sentAt: null, open: true, letterDocumentId: null, history: [] },
    { id: 'r-8', patientId: 'p-1', direction: 'Out', provider: 'Dr. Closed (synthetic)', urgency: 'Routine', reason: 'x', sections: [], state: 'Closed', channel: 'Print', sentAt: null, open: false, letterDocumentId: null, history: [] });
  await page.goto('/patients/p-1');
  const open_ = page.getByRole('region', { name: 'Open referrals' });
  await expect(open_).toContainText('Dr. S. Menon (synthetic)');
  await expect(open_).toContainText('Urgent');
  await expect(open_).toContainText('Sent');
  await expect(open_).not.toContainText('Dr. Closed (synthetic)');
});

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
for (const locale of ['en', 'ar'])
  test(`axe finds no violations on the referrals panel, the letter and Patient 360 (${locale})`, async ({ page }) => {
    const { referrals } = await open(page, { locale });
    referrals.push({ id: 'r-1', patientId: 'p-1', direction: 'Out', provider: 'Dr. S. Menon (synthetic)', urgency: 'Routine', reason: 'x', sections: ['Medicines'], state: 'Sent', channel: 'Print', sentAt: '2026-10-19T04:55:00Z', open: true, letterDocumentId: null,
      history: [{ state: 'Draft', at: '2026-10-19T04:50:00Z', documentId: null, note: null }, { state: 'Sent', at: '2026-10-19T04:55:00Z', documentId: null, note: null }] });
    for (const [path, ready] of [['/consult/e-1', 'Medicine list'], ['/consult/e-1/referrals/r-1/letter', ''], ['/patients/p-1', '']] as const) {
      await page.goto(path);
      if (ready === 'Medicine list') {
        await page.getByRole('button', { name: /Dr\. S\. Menon/ }).first().click();
        await expect(page.getByRole('list', { name: locale === 'ar' ? 'سجل الحالة' : 'Status history' })).toBeVisible();
      } else await expect(page.getByRole('article').or(page.getByRole('region', { name: locale === 'ar' ? 'الإحالات المفتوحة' : 'Open referrals' })).first()).toBeVisible();
      if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
      await page.addScriptTag({ path: axePath });
      const violations = await page.evaluate(async () =>
        (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations.map(v => `${v.id} (${v.nodes.length})`));
      expect(violations, path).toEqual([]);
    }
  });
