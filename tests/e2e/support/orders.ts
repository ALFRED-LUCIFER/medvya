import type { Page } from '@playwright/test';

// The API for one open consultation with investigation orders (MDV-P2-018), mocked in the shapes the server sends (IOrders.cs).
export const doctor = {
  user: { id: 'u-rao', displayName: 'Dr. Arjun Rao (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'SoloDoctor' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'SoloDoctor', flags: [] as string[], capabilities: { Patient360Chart: 'Full', PrescriptionsOrders: 'Sign' } },
};

type Order = { id: string; kind: string; name: string; note: string | null; state: string; destination: string | null; orderedAt: string | null };

export async function mockOrders(page: Page, { locale = 'en', ended = false, allowed = true }: { locale?: string; ended?: boolean; allowed?: boolean } = {}) {
  const orders: Order[] = [];
  const calls = { added: [] as unknown[], placed: [] as unknown[], included: [] as unknown[], sets: [] as unknown[] };
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: doctor }));
  await page.route('**/v1/notifications/inbox?*', r => r.fulfill({ json: { unread: 0, items: [] } }));
  await page.route('**/v1/encounters/e-1', r => r.fulfill({ json: {
    encounter: { id: 'e-1', appointmentId: 'a1', patientId: 'p-1', facilityId: 'f-1', leadClinicianId: 'u-rao', status: ended ? 'Ended' : 'Active', authorityMode: 'Medvya',
      startedAt: '2026-10-19T04:35:00Z', endedAt: ended ? '2026-10-19T04:55:00Z' : null, room: 'Room 3' },
    timeZone: 'Asia/Kolkata', treatmentConsent: true,
    patient: { id: 'p-1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female' },
    allergies: { state: 'NotRecorded', allergies: [] },
  } }));
  await page.route('**/v1/order-sets', r => r.fulfill({ json: [{ id: 's-1', name: 'Diabetes review labs', items: ['HbA1c', 'Fasting glucose'] }] }));
  await page.route('**/v1/encounters/e-1/orders', r => {
    if (!allowed) return r.fulfill({ status: 403, json: { code: 'no_access', requestPending: false } });
    if (r.request().method() === 'GET') return r.fulfill({ json: orders });
    const body = r.request().postDataJSON() as { kind: string; name: string; note: string | null };
    calls.added.push(body);
    orders.push({ id: `o-${orders.length + 1}`, kind: body.kind, name: body.name, note: body.note, state: 'Draft', destination: null, orderedAt: null });
    return r.fulfill({ json: orders });
  });
  await page.route('**/v1/encounters/e-1/orders/from-set', r => {
    calls.sets.push(r.request().postDataJSON());
    for (const name of ['HbA1c', 'Fasting glucose']) orders.push({ id: `o-${orders.length + 1}`, kind: 'Lab', name, note: null, state: 'Draft', destination: null, orderedAt: null });
    return r.fulfill({ json: orders });
  });
  await page.route('**/v1/orders/*/included', r => {
    const body = r.request().postDataJSON() as { included: boolean };
    calls.included.push(body);
    const id = new URL(r.request().url()).pathname.split('/')[3];
    const order = orders.find(o => o.id === id)!;
    order.state = body.included ? 'Draft' : 'NotIncluded';
    return r.fulfill({ json: orders });
  });
  await page.route('**/v1/encounters/e-1/orders/place', r => {
    const body = r.request().postDataJSON() as { destination: string | null };
    calls.placed.push(body);
    for (const o of orders.filter(x => x.state === 'Draft')) Object.assign(o, { state: 'Ordered', destination: body.destination, orderedAt: '2026-10-19T04:50:00Z' });
    return r.fulfill({ json: orders });
  });
  await page.route('**/v1/encounters/e-1/orders/slip', r => {
    const placed = orders.filter(o => o.state === 'Ordered');
    if (placed.length === 0) return r.fulfill({ status: 404, json: {} });
    return r.fulfill({ json: { patient: { fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', sexAtBirth: 'Female' },
      facilityName: 'Iyer Family Clinic · Adyar', doctorName: 'Dr. Arjun Rao (synthetic)', orderedAt: '2026-10-19T04:50:00Z', destination: placed[0]!.destination, orders: placed } });
  });
  return { orders, calls };
}
