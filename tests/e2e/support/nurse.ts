import type { Page } from '@playwright/test';

// MDV-P2-007: the nurse's session, board and intake endpoints, mocked in the shapes the server sends (CI has no API). The server rules
// (impossible values, flags, blockers, escalation, handoff) are proven on PostgreSQL by IntakeTests and IntakeEndpointTests; this mock
// applies the same two rules the screen depends on: a flagged reading blocks the handoff until it is kept, and an escalation needs a reason.
export const nurse = {
  user: { id: 'u-nurse', displayName: 'S. Thomas (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'Nurse' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Nurse', flags: ['clinical.nurse-intake'], country: 'IN',
    capabilities: { CheckInQueue: 'Full', VitalsAllergiesEntry: 'Full', Patient360Chart: 'Limited' } },
};
const at = '2026-10-19T04:30:00Z';
export const visit = {
  id: 'a1', reference: 'APT-A1', patientId: 'p-meera', patientName: 'Meera Iyer (synthetic)', dateOfBirth: '1972-03-12', registrationIncomplete: false,
  calendarId: 'u-rao', calendarName: 'Dr. Arjun Rao (synthetic)', visitType: 'FollowUp', reasonCategory: 'ChronicReview', start: at, end: at, state: 'WithNurse', since: at,
};

type Vital = { kind: string; value: number; flag: 'High' | 'Low' | 'Changed' | null; previousValue: number | null; previousAt: string | null; flagKept: boolean; recordedAt: string };
export type Intake = { vitals: Vital[]; escalated: boolean; state: string };

/** Mocks the nurse's API. `calls` records what the screen sent; `intake` is the server's current state. */
export async function mockNurse(page: Page, locale = 'en') {
  const calls = { saves: [] as unknown[], keeps: [] as string[], escalations: [] as unknown[], handoffs: 0 };
  const intake: Intake = { vitals: [], escalated: false, state: 'WithNurse' };
  const view = () => ({
    appointmentId: 'a1', patientId: 'p-meera', facilityId: 'f-1', state: intake.state, chiefComplaint: null, vitals: intake.vitals, escalated: intake.escalated,
    handedOff: intake.state !== 'WithNurse', blockers: intake.vitals.some(v => v.flag && !v.flagKept) ? ['vitals_undecided'] : [],
  });
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: nurse }));
  await page.route('**/v1/front-desk/board?*', r => r.fulfill({ json: { facilityId: 'f-1', timeZone: 'Asia/Kolkata', date: '2026-10-19', now: '2026-10-19T04:40:00Z', cards: [{ ...visit, state: intake.state }] } }));
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
  await page.route('**/v1/intake/a1/escalate', r => {
    calls.escalations.push(r.request().postDataJSON());
    intake.escalated = true;
    return r.fulfill({ json: view() });
  });
  await page.route('**/v1/intake/a1/handoff', r => {
    calls.handoffs++;
    intake.state = 'ReadyForDoctor';
    return r.fulfill({ json: view() });
  });
  return { calls, intake };
}
