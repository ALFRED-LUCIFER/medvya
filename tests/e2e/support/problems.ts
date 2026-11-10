import type { Page } from '@playwright/test';

// MDV-P2-010 (E-07) without an API: the consultation, its identity banner, a small code set and the problem list in the shapes the server sends.
// The mock keeps only what the screen leans on (a code fills the description; "added here" for a new row); the real rules are proven on
// PostgreSQL (ProblemListTests).
const at = '2026-10-19T04:35:00Z';
const doctor = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [] as string[],
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'Full', ClinicalNotes: 'Sign' } },
};
const codes = [
  { code: 'E11.9', title: 'Type 2 diabetes mellitus without complications' },
  { code: 'E78.5', title: 'Hyperlipidaemia, not specified' },
  { code: 'I10', title: 'Essential (primary) hypertension, high blood pressure' },
  { code: 'I20.9', title: 'Angina, not specified' },
];
type Row = {
  id: string; code: string | null; codeSystem: string | null; codeSystemVersion: string | null; description: string; verification: string; status: string;
  onsetOn: string | null; source: string; addedHere: boolean; confirmed: boolean; recordedAt: string;
};
const coded = (code: string, title: string) => ({ code, codeSystem: 'ICD-10', codeSystemVersion: '2019-medvya-starter-1', description: title });

export async function mockProblems(page: Page, locale = 'en', ended = false) {
  const rows: Row[] = [
    { id: 'pr-1', ...coded('E11.9', codes[0].title), verification: 'Verified', status: 'Active', onsetOn: '2019-06-01', source: 'ClinicianVerified', addedHere: false, confirmed: true, recordedAt: at },
    { id: 'pr-2', code: null, codeSystem: null, codeSystemVersion: null, description: 'Knee pain since a fall (synthetic)', verification: 'PatientReported', status: 'Active', onsetOn: null, source: 'PatientReported', addedHere: false, confirmed: false, recordedAt: at },
  ];
  const calls = { adds: [] as unknown[], updates: [] as unknown[] };
  const view = () => ({ encounterId: 'e-1', patientId: 'p-meera', rows, locked: ended });
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
  await page.route('**/v1/encounters/e-1', r => r.fulfill({ json: {
    encounter: { id: 'e-1', appointmentId: 'a1', patientId: 'p-meera', facilityId: 'f-1', leadClinicianId: 'u-rao', status: ended ? 'Ended' : 'Active', authorityMode: 'Medvya', startedAt: at, endedAt: ended ? at : null, room: 'Room 3' },
    timeZone: 'Asia/Kolkata', treatmentConsent: true,
    patient: { id: 'p-meera', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-14', ageYears: 54, sexAtBirth: 'Female' },
    allergies: { state: 'NotRecorded', allergies: [] },
  } }));
  await page.route('**/v1/diagnosis-codes', r => r.fulfill({ json: { system: 'ICD-10', version: '2019-medvya-starter-1', codes } }));
  await page.route('**/v1/encounters/e-1/problems', r => {
    if (r.request().method() === 'GET') return r.fulfill({ json: view() });
    const body = r.request().postDataJSON() as { code: string | null; description: string | null; verification: string; onsetOn: string | null };
    calls.adds.push(body);
    const code = codes.find(c => c.code === body.code);
    rows.push({
      id: `pr-${rows.length + 1}`, code: code?.code ?? null, codeSystem: code ? 'ICD-10' : null, codeSystemVersion: code ? '2019-medvya-starter-1' : null,
      description: body.description ?? code!.title, verification: body.verification, status: 'Active', onsetOn: body.onsetOn,
      source: body.verification === 'PatientReported' ? 'PatientReported' : 'ClinicianVerified', addedHere: true, confirmed: body.verification === 'Verified', recordedAt: at,
    });
    return r.fulfill({ json: view() });
  });
  await page.route('**/v1/encounters/e-1/problems/*', r => {
    const body = r.request().postDataJSON() as { verification: string; status: string; onsetOn: string | null };
    calls.updates.push({ id: r.request().url().split('/').pop(), ...body });
    const row = rows.find(x => r.request().url().endsWith(x.id))!;
    Object.assign(row, body, { confirmed: body.verification === 'Verified' });
    return r.fulfill({ json: view() });
  });
  return { calls };
}
