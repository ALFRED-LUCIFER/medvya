import { expect, test, type Page } from '@playwright/test';
import { mockReconciliation } from '../support/reconciliation';
import { mockNote } from '../support/note';
import { authorised, clearChecks, line, medicine, mockPrescription, penicillinAlert, stopLine } from '../support/prescription';
import { mockShares } from '../support/share';
import { mockProblems } from '../support/problems';
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

// Steps 9 to 12 (MDV-P2-045): Meera's one-page chart with her penicillin allergy and the consultation e-1 open; on the real stack in fullstack/j2.spec.ts.
const meera = { id: 'p-a1', fullName: 'Meera Iyer', mrn: '000001', dateOfBirth: '1972-03-14', ageYears: 54, sexAtBirth: 'Female', preferredLanguage: null };
const penicillin = { substance: 'Penicillin', reaction: 'Rash', status: 'PatientReported', source: 'PatientReported' };
async function mockChart(page: Page) {
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
  await page.route('**/v1/patients/p-a1/chart', r => r.fulfill({ json: { patient: meera, treatmentConsent: true, timeZone: 'Asia/Kolkata', lifeStage: 'Adult', historySections: [],
    chart: { allergyState: 'Listed', allergies: [penicillin], medicines: [], problems: [], lastVitals: [], lastLabs: [], bmi: null, trends: [], recentEncounters: [], openEncounterId: 'e-1', history: [], bloodGroup: null, careTeam: [] } } }));
  await page.route('**/v1/encounters/e-1', r => r.fulfill({ json: { timeZone: 'Asia/Kolkata', treatmentConsent: true, patient: meera, allergies: { state: 'Listed', allergies: [penicillin] },
    encounter: { id: 'e-1', appointmentId: 'a1', patientId: 'p-a1', facilityId: 'f-1', leadClinicianId: 'u-rao', status: 'Active', authorityMode: 'Medvya', startedAt: at, endedAt: null, room: 'Room 3' } } }));
}

// J2.5 is "Resolve conflicts & sign": signing is MDV-P2-012, so the step stays pending below. Its conflict half is built (MDV-P2-011, E-07):
// the statin the chart says is active and the patient says stopped is decided before the note is signed.
test('J2.5 (conflict half, MDV-P2-011) E-07 · Doctor: statin stopped vs chart active is decided', async ({ page }) => {
  const { calls } = await mockReconciliation(page);
  await page.goto('/consult/e-1/medicines');
  await expect(page.getByText('Atorvastatin active in the chart, patient says stopped')).toBeVisible();
  await page.getByRole('group', { name: 'Decision for Atorvastatin' }).getByRole('button', { name: 'Stop' }).click();
  await page.getByLabel('Reason').fill('Muscle pain (synthetic)');
  await page.getByRole('button', { name: 'Save decision' }).click();
  await expect(page.getByText('Decided: Stop · Muscle pain (synthetic)')).toBeVisible();
  expect(calls.decisions).toEqual([{ key: 'atorvastatin', kind: 'Stop', newDose: '', reason: 'Muscle pain (synthetic)' }]);
});

// J2.6 is "Authorise Rx & orders" (the step below, MDV-P2-017). Its composer half (MDV-P2-015, E-05): the
// statin stopped in reconciliation is a discontinue line, and the doctor adds metformin from the formulary as a draft. The safety checks that
// authorising will require (MDV-P2-016, E-11) run on the draft: Meera has no known allergies and metformin raises nothing.
test('J2.6 (composer and safety-check halves, MDV-P2-015/016) E-05 · Doctor: the stopped statin is a discontinue line beside the new, checked draft', async ({ page }) => {
  const { calls } = await mockPrescription(page, { lines: [stopLine('Atorvastatin', 'Muscle pain (synthetic)')], checks: clearChecks() });
  await page.goto('/consult/e-1');
  await expect(page.getByRole('list', { name: 'Medicines to stop' })).toContainText('Atorvastatin');
  const rx = page.getByRole('region', { name: 'Prescription' });
  await rx.getByLabel('Medicine', { exact: true }).fill('metformin');
  await rx.getByRole('button', { name: 'Add Metformin 500 mg Tablet' }).click();
  expect(calls.added).toEqual([{ formularyItemId: 'fi-metformin' }]);
  await expect(rx).toContainText('Draft, not authorised');
  const checks = page.getByRole('region', { name: 'Safety checks' });
  await expect(checks).toContainText('Allergies: no known allergies (recorded)');
  await expect(checks).toContainText('No alerts from the checks that could run.');
});

// The coding half of J2.5 (MDV-P2-010, E-07): before signing, the doctor codes Meera's diagnosis with ICD-10; a provisional one says so.
test('J2.5 (coding half, MDV-P2-010) E-07 · Doctor: the diagnosis is coded with ICD-10 and a verification state', async ({ page }) => {
  const { calls } = await mockProblems(page);
  await page.goto('/consult/e-1/problems');
  await page.getByLabel('Search ICD-10 by code or words').fill('hypertension');
  await page.getByRole('list', { name: 'Matching codes' }).getByRole('button', { name: /I10/ }).click();
  await page.getByRole('button', { name: 'Add problem' }).click();
  await expect(page.getByRole('region', { name: 'Problem list' }).getByRole('row', { name: /hypertension/ })).toContainText('Added today · verified');
  expect(calls.adds).toEqual([{ code: 'I10', description: null, verification: 'Verified', onsetOn: null }]);
});

journey('J2', {
  5: 'MDV-P2-012',
  // E-01 (MDV-P2-033): the chart on one page, the allergy in the banner, the section buttons, and the open consultation one click away.
  9: async ({ page }) => {
    await mockChart(page);
    await page.goto('/patients/p-a1');
    await expect(page.getByRole('region', { name: 'Patient Meera Iyer' })).toContainText('Penicillin');
    await expect(page.getByRole('group', { name: 'Show part of the chart' }).getByRole('button', { name: 'Everything' })).toHaveAttribute('aria-pressed', 'true');
    await expect(page.getByRole('link', { name: 'Open consultation' })).toHaveAttribute('href', '/consult/e-1');
  },
  // E-05 / E-11 (MDV-P2-039): Add medicine on the Medicines card; amoxicillin is blocked by the penicillin allergy until a reason is given.
  10: async ({ page }) => {
    const formulary = [medicine('fi-amox', 'Amoxicillin', '500 mg', 'Capsule')];
    await mockChart(page);
    await mockPrescription(page, { formulary, checksOf: lines => {
      const amox = lines.find(l => l.included);
      return clearChecks({ allergies: 'Listed', alerts: amox ? [penicillinAlert(amox.id!)] : [], unresolved: amox ? 1 : 0 });
    } }); // registered after the chart, so its session (which may prescribe) is the one answered
    await page.goto('/patients/p-a1');
    await page.getByRole('region', { name: 'Medicines', exact: true }).getByRole('button', { name: 'Add medicine' }).click();
    const panel = page.getByRole('dialog', { name: 'Add medicine' });
    await panel.getByLabel('Search medicine').fill('amox');
    await panel.getByRole('button', { name: /Amoxicillin · Capsule/ }).click();
    await panel.getByRole('group', { name: 'How often' }).getByRole('button', { name: '1-1-1 · three times a day', exact: true }).click();
    await expect(panel.getByText('A serious finding blocks this medicine.', { exact: false })).toBeVisible();
    await expect(panel.getByRole('button', { name: 'Add to prescription' })).toBeDisabled();
  },
  // E-04 (MDV-P2-041, MDV-P2-044): the one-page consultation; Sign stays locked until the checklist is complete (here, the recording consent).
  11: async ({ page }) => {
    await mockChart(page);
    await mockNote(page, { version: 1, sections: { chiefComplaint: 'Review of sugars', assessment: 'Type 2 diabetes', plan: 'Continue metformin' } });
    await page.goto('/consult/e-1');
    const note = page.getByRole('region', { name: 'Consultation note' });
    await expect(note.getByRole('navigation', { name: 'Note sections' }).getByRole('link')).toHaveCount(7);
    const sign = note.getByRole('button', { name: 'Sign note' });
    await expect(sign).toBeDisabled();
    await expect(sign).toHaveAccessibleDescription('1 item still needs your review before signing');
    await note.getByRole('radio', { name: 'Patient declined: I type the note' }).check();
    await expect(sign).toHaveAccessibleDescription('Everything reviewed. Ready to sign.');
  },
  // E-06 (MDV-P2-012): signed after the step-up; the gates are proven on PostgreSQL in ConsultNoteTests.
  12: async ({ page }) => {
    await mockChart(page);
    const mock = await mockNote(page, { version: 1, recordingConsent: 'Declined', sections: { chiefComplaint: 'Review of sugars', assessment: 'Type 2 diabetes', plan: 'Continue metformin' } });
    mock.reauthed = true;
    await page.goto('/consult/e-1');
    const note = page.getByRole('region', { name: 'Consultation note' });
    await note.getByRole('button', { name: 'Sign note' }).click();
    await page.getByRole('dialog').getByRole('button', { name: /^Sign as / }).click();
    await expect(note.getByRole('status').filter({ hasText: 'SIGNED v1' })).toBeVisible();
  },
  // E-05: the prescription and the orders are approved separately, each by its own action and neither by signing the note (MDV-P2-017, MDV-P2-018).
  // Authorising asks for the step-up first; the gates themselves are proven on PostgreSQL in RxAuthorisationTests.
  6: async ({ page }) => {
    const { calls, orders, orderCalls } = await mockPrescription(page, {
      lines: [line('rx-1', 'Metformin', { dose: '1', doseUnit: 'tablet', route: 'oral', frequency: 'BD', sig: { en: 'Take 1 tablet by mouth twice a day.', hi: '', ta: '', ar: '' } }),
        stopLine('Atorvastatin', 'Muscle pain (synthetic)')],
      checks: clearChecks(), reauthFirst: true,
    });
    await page.goto('/consult/e-1');
    const rx = page.getByRole('region', { name: 'Prescription' });
    await rx.getByRole('button', { name: 'Authorise prescription' }).click();
    await expect(rx.getByRole('alert')).toContainText('Confirm your identity to authorise.');
    await rx.getByRole('button', { name: 'Authorise prescription' }).click(); // after the step-up
    await expect(rx).toContainText('Authorised · version 1');
    expect(orders.every(o => o.state === 'Draft')).toBe(true); // authorising the prescription placed nothing

    await page.getByLabel('Test', { exact: true }).fill('HbA1c');
    await page.getByRole('button', { name: 'Add test' }).click();
    await page.getByRole('button', { name: 'Place orders' }).click();
    await expect(page.getByText('Orders placed.')).toBeVisible();
    expect(orderCalls.placed).toEqual([{ destination: null }]); // its own action
    expect(calls.authorised).toBe(2);
  },
  // E-10: the authorised prescription goes to Meera by a secure link (the message names only the clinic; the link opens with a code to her mobile)
  // and to her pharmacy, whose dispatch is recorded (MDV-P2-020). The gates and the message texts are proven on PostgreSQL in RxShareTests.
  7: async ({ page }) => {
    await mockPrescription(page, { lines: [line('rx-1', 'Metformin')], authorisation: { authorised: authorised(), changed: false } });
    const { calls } = await mockShares(page);
    await page.goto('/consult/e-1/rx/print');
    const share = page.getByRole('region', { name: 'Share with the patient or a pharmacy' });
    await share.getByRole('button', { name: 'Send secure link' }).click();
    await expect(share.getByRole('row', { name: /Patient, by SMS/ })).toBeVisible();
    await share.getByRole('combobox', { name: 'Send to' }).click();
    await page.getByRole('option', { name: 'A pharmacy, by email' }).click();
    await share.getByLabel('Pharmacy name').fill('Apollo Pharmacy Adyar (synthetic)');
    await share.getByLabel('Pharmacy email').fill('orders@pharmacy.example.test');
    await share.getByRole('button', { name: 'Send secure link' }).click();
    await expect(share.getByRole('row', { name: /Apollo Pharmacy Adyar/ })).toContainText('Sent to the pharmacy');
    expect(calls.shared).toHaveLength(2);
  },
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
      patient: { id: 'p-meera', fullName: 'Meera Iyer', mrn: '000001', dateOfBirth: '1972-03-14', ageYears: 54, sexAtBirth: 'Female' },
      allergies: { state: 'NotRecorded', allergies: [] } } }));
    await page.route('**/v1/chart/allergy-flags', r => r.fulfill({ json: { items: [{ patientId: 'p-a1', state: 'NotRecorded' }] } }));
    await page.goto('/today');
    await expect(page.getByRole('region', { name: 'Next patient' })).toContainText('Meera Iyer');
    await page.getByRole('region', { name: 'Next patient' }).getByRole('button', { name: 'Start consultation with Meera Iyer' }).click();
    const banner = page.getByRole('region', { name: 'Patient identity' });
    await expect(banner.getByRole('heading', { name: 'Meera Iyer' })).toBeVisible();
    await expect(banner).toContainText('Room 3');
    // MDV-P2-009: the note opens beside the banner, ready to type (no recording, so the manual path).
    await mockNote(page);
    await page.reload();
    await expect(page.getByRole('region', { name: 'Consultation note' }).getByLabel('Chief complaint')).toBeVisible();
  },
  // E-01: the 60-second brief is the verified chart (MDV-P2-004): allergy first, then problems and medicines, each with its source.
  // shortcut: the "AI summary · not reviewed" half of the split is P3 (A-01) and is not shown; only the verified side exists.
  2: async ({ page }) => {
    await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
    await page.route('**/v1/patients/p-meera/chart', r => r.fulfill({ json: {
      patient: { id: 'p-meera', fullName: 'Meera Iyer', mrn: '000001', dateOfBirth: '1972-03-14', ageYears: 54, sexAtBirth: 'Female', preferredLanguage: null }, treatmentConsent: true, timeZone: 'Asia/Kolkata', lifeStage: 'Adult', historySections: ['PastMedical', 'Surgical', 'Family', 'Social', 'WomensHealth'], 
      chart: { allergyState: 'Listed', allergies: [{ substance: 'Penicillin', reaction: null, status: 'Confirmed', source: 'ClinicianVerified' }],
        problems: [{ icd10Code: 'E11.9', description: 'Type 2 diabetes mellitus', verification: 'Verified', onsetOn: null, source: 'ClinicianVerified' }],
        medicines: [{ name: 'Metformin', dose: '500 mg', state: 'Active', source: 'Imported' }], lastVitals: [],
        trends: [{ code: 'HbA1c', unit: '%', points: [{ value: 7.4, takenAt: at, source: 'SyncedFromHis' }] }], recentEncounters: [], openEncounterId: null, lastLabs: [{ code: 'HbA1c', value: 7.4, unit: '%', takenAt: at, source: 'SyncedFromHis', flag: null, range: null }], bmi: null, history: [], bloodGroup: null, careTeam: [] } } }));
    await page.goto('/patients/p-meera');
    const banner = page.getByRole('region', { name: 'Patient Meera Iyer' });
    await expect(banner).toContainText('Penicillin');
    await expect(page.getByRole('region', { name: 'Problems', exact: true })).toContainText('Clinician verified');
    await expect(page.getByRole('region', { name: 'Medicines', exact: true })).toContainText('Imported');
    await expect(page.getByRole('region', { name: 'Results and trends', exact: true }).getByRole('img', { name: /HbA1c: latest .?7.4 %/ })).toBeVisible();
  },
});
