import type { Page } from '@playwright/test';

// MDV-P2-011 (E-07) without an API: the consultation, its identity banner and a reconciliation table in the shapes the server sends. The mock keeps
// the one rule the screen leans on (confirm refused until every row is decided); the real rules are proven on PostgreSQL (MedicationReconciliationTests).
const at = '2026-10-19T04:35:00Z'; // 10:05 IST
const doctor = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [] as string[],
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', Patient360Chart: 'Full', ClinicalNotes: 'Sign' } },
};
const side = (dose: string | null, state = 'Active') => ({ dose, frequency: null, state, needsClarification: false });
type Row = {
  key: string; name: string; chart: unknown; patient: unknown; other: unknown; conflict: string | null; allowed: string[];
  decision: { kind: string; newDose: string | null; reason: string | null; decidedAt: string } | null;
};
const rows = (): Row[] => [
  { key: 'atorvastatin', name: 'Atorvastatin', chart: side('20 mg'), patient: side(null, 'Stopped'), other: null, conflict: 'State', allowed: ['Keep', 'Change', 'Stop'], decision: null },
  { key: 'metformin', name: 'Metformin', chart: side('500 mg'), patient: side('1000 mg'), other: null, conflict: 'Dose', allowed: ['Keep', 'Change', 'Stop'], decision: null },
  { key: 'aspirin', name: 'Aspirin', chart: null, patient: side('75 mg'), other: null, conflict: 'NotOnChart', allowed: ['Add', 'Ignore'], decision: null },
];

export async function mockReconciliation(page: Page, locale = 'en') {
  const state = { rows: rows(), confirmedAt: null as string | null };
  const calls = { decisions: [] as unknown[], confirms: 0 };
  const view = () => ({
    encounterId: 'e-1', patientId: 'p-meera', rows: state.rows, decided: state.rows.filter(r => r.decision).length, total: state.rows.length,
    disagree: state.rows.filter(r => r.conflict).length, confirmedAt: state.confirmedAt,
  });
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
  await page.route('**/v1/encounters/e-1', r => r.fulfill({ json: {
    encounter: { id: 'e-1', appointmentId: 'a1', patientId: 'p-meera', facilityId: 'f-1', leadClinicianId: 'u-rao', status: 'Active', authorityMode: 'Medvya', startedAt: at, endedAt: null, room: 'Room 3' },
    timeZone: 'Asia/Kolkata', treatmentConsent: true,
    patient: { id: 'p-meera', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-14', ageYears: 54, sexAtBirth: 'Female' },
    allergies: { state: 'NotRecorded', allergies: [] },
  } }));
  await page.route('**/v1/encounters/e-1/medicines/reconciliation', r => r.fulfill({ json: view() }));
  await page.route('**/v1/encounters/e-1/medicines/reconciliation/decisions', r => {
    const body = r.request().postDataJSON() as { key: string; kind: string; newDose: string; reason: string };
    calls.decisions.push(body);
    const row = state.rows.find(x => x.key === body.key)!;
    row.decision = { kind: body.kind, newDose: body.kind === 'Change' ? body.newDose : null, reason: body.reason || null, decidedAt: at };
    return r.fulfill({ json: view() });
  });
  await page.route('**/v1/encounters/e-1/medicines/reconciliation/confirm', r => {
    calls.confirms++;
    if (state.rows.some(x => !x.decision)) return r.fulfill({ status: 409, json: { code: 'undecided' } });
    state.confirmedAt = at;
    return r.fulfill({ json: view() });
  });
  return { calls };
}
