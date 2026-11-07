import { expect, type Page } from '@playwright/test';

// MDV-P1-004, MDV-P1-061 (6 Nov steps). CI has no API: the sign-up endpoints are mocked with the synthetic cast (docs/00-overview/personas.md).
export const rules = {
  currency: 'INR',
  authorities: [{ code: 'TNMC', name: 'Tamil Nadu Medical Council' }, { code: 'KMC', name: 'Karnataka Medical Council' }],
  slotMinutes: [10, 15, 20, 30, 45, 60],
  templates: ['general-opd', 'diabetes-review', 'hypertension', 'paediatrics', 'antenatal', 'teleconsult'],
  specialties: [{ code: 'general-practice', name: 'General practice' }, { code: 'paediatrics', name: 'Paediatrics' }, { code: 'diabetes', name: 'Diabetes' }],
};

const newUser = { user: { id: 'u-arjun', displayName: 'New user' }, memberships: [], active: null };
const doctor = {
  user: { id: 'u-arjun', displayName: 'Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Rao Clinic (synthetic)', facilityId: 'f-1', facilityName: 'Rao Clinic (synthetic)', role: 'SoloDoctor' }],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [], country: 'IN', licencePending: true,
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', InvoicesPayments: 'Full', Patient360Chart: 'Full', VitalsAllergiesEntry: 'Full' },
  },
};

export type Saved = { step: number; validate?: number; data: Record<string, unknown> };

/**
 * A fake onboarding API. `draft` is what the server holds (null = not started). Saves are recorded; a licence number of "1" fails the
 * practice step's check, as the real server does.
 */
export async function mockOnboarding(page: Page, draft: { step: number; data: Record<string, unknown> } | null = null) {
  const state = { draft, saves: [] as Saved[], finished: false, chose: '', signedOut: false };
  const view = () => ({ country: 'IN', step: state.draft!.step, data: state.draft!.data, rules });
  await page.route('**/v1/me/context', r => state.signedOut ? r.fulfill({ status: 401 }) : r.fulfill({ json: state.finished && state.chose ? doctor : newUser }));
  await page.route('**/bff/logout', r => { state.signedOut = true; return r.fulfill({ status: 204 }); });
  await page.route('**/v1/me/facility', r => { state.chose = (r.request().postDataJSON() as { membershipId: string }).membershipId; return r.fulfill({ status: 204 }); });
  await page.route('**/v1/onboarding', r => {
    const method = r.request().method();
    if (method === 'GET') return state.draft ? r.fulfill({ json: view() }) : r.fulfill({ status: 204 });
    if (method === 'POST') { state.draft = { step: 1, data: {} }; return r.fulfill({ json: view() }); }
    const body = r.request().postDataJSON() as Saved;
    if (body.validate === 1 && (body.data.licence as { number?: string } | undefined)?.number === '1')
      return r.fulfill({ status: 400, json: { errors: { 'licence.number': ['format'] } } });
    state.saves.push(body);
    state.draft = { step: body.step, data: body.data };
    return r.fulfill({ status: 204 });
  });
  await page.route('**/v1/onboarding/finish', r => {
    state.finished = true;
    return r.fulfill({ json: { membershipId: 'm-1', role: 'SoloDoctor', invitationsSent: 1, invitationsFailed: 0 } });
  });
  return state;
}

/** Fills the five steps for a solo doctor in India, invites the front desk, finishes and opens Today from the ready screen. */
export async function completeWizard(page: Page) {
  await page.getByRole('button', { name: 'Start setup' }).click();
  await page.getByLabel('Full name').fill('Arjun Rao (synthetic)');
  await page.getByLabel('Practice name').fill('Rao Clinic (synthetic)');
  await page.getByLabel(/^Address/).fill('12 Test Street, Chennai');
  await page.getByLabel('General practice').check();
  await page.getByRole('combobox', { name: 'Medical council' }).click();
  await page.getByRole('option', { name: /TNMC/ }).click();
  await page.getByLabel('Registration number').fill('84213');
  await page.getByRole('button', { name: 'Continue' }).click();

  await expect(page.getByRole('heading', { level: 1, name: 'When are you open, and what do you charge?' })).toBeVisible();
  await page.getByRole('button', { name: 'Continue' }).click(); // hours and fees: the design defaults

  await page.getByLabel('Email address').fill('joseph@example.test');
  await page.getByRole('button', { name: 'Invite', exact: true }).click(); // front desk is the default role
  await expect(page.getByRole('list', { name: 'Invited' })).toContainText('joseph@example.test');
  await page.getByRole('button', { name: 'Continue' }).click();

  await page.getByLabel('Start fresh').check();
  await page.getByRole('button', { name: 'Continue' }).click();

  await page.getByRole('button', { name: 'Finish setup' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Rao Clinic (synthetic) is ready.' })).toBeVisible();
  await page.getByRole('button', { name: 'Open Today' }).click();
}
