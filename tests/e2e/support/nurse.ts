import type { Page } from '@playwright/test';

// MDV-P2-007 / MDV-P2-008: the nurse's session, board and intake endpoints, mocked in the shapes the server sends (CI has no API). The server
// rules (impossible values, flags, blockers, escalation, allergy gate, handoff) are proven on PostgreSQL by IntakeTests, AllergyIntakeTests and
// IntakeEndpointTests; this mock applies the same rules the screen depends on: a flagged reading, an allergy without a status and an
// unconfirmed "no known allergies" each block the handoff, and an escalation needs a reason.
export const nurse = {
  user: { id: 'u-nurse', displayName: 'S. Thomas (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'Nurse' }],
  active: { membershipId: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', role: 'Nurse', flags: ['clinical.nurse-intake'], country: 'IN',
    capabilities: { CheckInQueue: 'Full', VitalsAllergiesEntry: 'Full', Patient360Chart: 'Limited' } },
};
const at = '2026-10-19T04:30:00Z';
export const visit = {
  id: 'a1', reference: 'APT-A1', patientId: 'p-meera', patientName: 'Meera Iyer (synthetic)', dateOfBirth: '1972-03-12', registrationIncomplete: false,
  calendarId: 'u-rao', calendarName: 'Dr. Arjun Rao (synthetic)', visitType: 'FollowUp', reasonCategory: 'ChronicReview', start: at, end: at, state: 'WithNurse', since: at,
};

type Vital = { kind: string; value: number; flag: 'High' | 'Low' | 'Changed' | null; previousValue: number | null; previousAt: string | null; flagKept: boolean; recordedAt: string };
type Allergy = { id: string; substance: string; reaction: string | null; status: string | null; source: string; statusSetAt: string | null };
type Medicine = { id: string; name: string; dose: string | null; frequency: string | null; needsClarification: boolean; source: string; recordedAt: string };
export type Intake = { vitals: Vital[]; allergies: Allergy[]; medicines: Medicine[]; noKnown: boolean; escalated: boolean; state: string };

/**
 * Mocks the nurse's API. `calls` records what the screen sent; `intake` is the server's current state. `unreviewed`: the patient has an allergy
 * nobody has set a status for. `checkedIn`: the visit is still Checked in, for the nurse to take to intake (#956).
 */
export async function mockNurse(page: Page, locale = 'en', { unreviewed = false, checkedIn = false }: { unreviewed?: boolean; checkedIn?: boolean } = {}) {
  const calls = { moves: [] as unknown[], saves: [] as unknown[], keeps: [] as string[], escalations: [] as unknown[], handoffs: 0, none: 0, allergies: [] as unknown[], statuses: [] as unknown[], medicines: [] as unknown[] };
  const intake: Intake = {
    vitals: [], medicines: [], noKnown: false, escalated: false, state: checkedIn ? 'CheckedIn' : 'WithNurse',
    allergies: unreviewed ? [{ id: 'al-1', substance: 'Penicillin (synthetic)', reaction: null, status: null, source: 'PatientReported', statusSetAt: null }] : [],
  };
  const view = () => ({
    appointmentId: 'a1', patientId: 'p-meera', facilityId: 'f-1', state: intake.state, chiefComplaint: null, vitals: intake.vitals, allergies: intake.allergies, medicines: intake.medicines,
    noKnownAllergiesConfirmed: intake.allergies.length === 0 && intake.noKnown, escalated: intake.escalated, handedOff: intake.state !== 'WithNurse',
    blockers: [
      ...(intake.vitals.some(v => v.flag && !v.flagKept) ? ['vitals_undecided'] : []),
      ...(intake.allergies.some(a => a.status === null) ? ['allergy_status_required'] : intake.allergies.length === 0 && !intake.noKnown ? ['allergies_not_confirmed'] : []),
    ],
  });
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: nurse }));
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: '2026-10-19T04:40:00Z', cards: [{ ...visit, state: intake.state }] } }));
  await page.route('**/v1/appointments/a1/status', r => {
    const { state } = r.request().postDataJSON() as { state: string };
    calls.moves.push(state);
    intake.state = state;
    return r.fulfill({ json: { ...visit, state } });
  });
  await page.route('**/v1/intake/a1', async r => {
    if (r.request().method() === 'PUT') {
      const body = r.request().postDataJSON() as { vitals: { kind: string; value: number }[] };
      calls.saves.push(body);
      const bad = body.vitals.findIndex(v => v.kind === 'OxygenSaturation' && v.value > 100);
      if (bad >= 0) return r.fulfill({ status: 400, contentType: 'application/json', json: { errors: { [`vitals[${bad}].value`]: ['implausible'] } } });
      intake.vitals = body.vitals.map(v => ({
        kind: v.kind, value: v.value, previousValue: v.kind === 'Weight' ? 70 : null, previousAt: v.kind === 'Weight' ? '2026-10-02T04:30:00Z' : null, flagKept: false, recordedAt: at,
        flag: v.kind === 'SystolicBp' && v.value >= 140 ? 'High' : v.kind === 'Weight' && v.value - 70 >= 2 ? 'Changed' : null,
      }));
      return r.fulfill({ json: view() });
    }
    return r.fulfill({ json: { intake: view(), timeZone: 'Asia/Kolkata',
      patient: { id: 'p-meera', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female' } } });
  });
  await page.route('**/v1/intake/a1/vitals/*/keep', r => {
    const kind = r.request().url().split('/').at(-2)!;
    calls.keeps.push(kind);
    intake.vitals = intake.vitals.map(v => (v.kind === kind ? { ...v, flagKept: true } : v));
    return r.fulfill({ json: view() });
  });
  await page.route('**/v1/intake/a1/allergies', r => {
    const body = r.request().postDataJSON() as { substance: string; reaction: string; status: string };
    calls.allergies.push(body);
    intake.allergies = [...intake.allergies, { id: `al-${intake.allergies.length + 1}`, substance: body.substance, reaction: body.reaction || null, status: body.status, source: 'PatientReported', statusSetAt: at }];
    return r.fulfill({ json: view() });
  });
  await page.route('**/v1/intake/a1/allergies/*/status', r => {
    const id = r.request().url().split('/').at(-2)!;
    const { status } = r.request().postDataJSON() as { status: string };
    calls.statuses.push({ id, status });
    intake.allergies = intake.allergies.map(a => (a.id === id ? { ...a, status, statusSetAt: at } : a));
    return r.fulfill({ json: view() });
  });
  await page.route('**/v1/intake/a1/no-known-allergies', r => {
    calls.none++;
    intake.noKnown = true;
    return r.fulfill({ json: view() });
  });
  await page.route('**/v1/intake/a1/medicines', r => {
    const body = r.request().postDataJSON() as { name: string; dose: string; frequency: string; needsClarification: boolean };
    calls.medicines.push(body);
    intake.medicines = [...intake.medicines, { id: `m-${intake.medicines.length + 1}`, name: body.name, dose: body.dose || null, frequency: body.frequency || null, needsClarification: body.needsClarification, source: 'PatientReported', recordedAt: at }];
    return r.fulfill({ json: view() });
  });
  await page.route('**/v1/intake/a1/escalate', r => {
    calls.escalations.push(r.request().postDataJSON());
    intake.escalated = true;
    return r.fulfill({ json: view() });
  });
  await page.route('**/v1/intake/a1/handoff', r => {
    if (view().blockers.length > 0) return r.fulfill({ status: 409, contentType: 'application/json', json: { code: 'handoff_blocked', blockers: view().blockers } });
    calls.handoffs++;
    intake.state = 'ReadyForDoctor';
    return r.fulfill({ json: view() });
  });
  return { calls, intake };
}
