import type { Page } from '@playwright/test';

// MDV-P4-002: Settings, Services and Settings, Billing with the API mocked in the shapes the server sends (ServiceView, PriceVersionView, BillingSettingsView).
// Synthetic clinic, doctor and prices only.
export const billingOwner = (country: 'IN' | 'AE' = 'IN', flags: string[] = ['billing.invoices']) => ({
  user: { id: 'u-o', displayName: 'K. Lakshmi (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'PracticeAdmin' }],
  active: { membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'PracticeAdmin', country, flags, capabilities: { StaffRolesIntegrations: 'Config', InvoicesPayments: 'Full' } },
});

type Service = Record<string, unknown> & { id: string; name: string };

export async function mockBilling(page: Page, { country = 'IN', flags, locale = 'en' }: { country?: 'IN' | 'AE'; flags?: string[]; locale?: string } = {}) {
  const currency = country === 'IN' ? 'INR' : 'AED';
  const consultation: Service = {
    id: 'svc-new', name: 'Consultation (new visit)', category: 'Consultation', code: null, sac: '999312', taxTreatment: 'Exempt', gstRateBasisPoints: null,
    durationMinutes: 15, consultationKind: 'New', archivedAt: null, clinicPriceMinor: 50_000, currency, version: 1,
  };
  const services: Service[] = [consultation];
  const prices: Record<string, Record<string, unknown>[]> = {
    'svc-new': [{ id: 'pv-1', doctorId: null, amountMinor: 50_000, currency, effectiveFrom: '2026-09-01T00:00:00Z', createdAt: '2026-09-01T00:00:00Z' }],
  };
  const posts: { path: string; body: unknown }[] = [];
  // MDV-P4-007: the clinic's Razorpay connection; secrets go in, only the status comes back.
  let razorpay: Record<string, unknown> = { connected: false, mode: null, keyIdEnding: null, webhookPath: null };
  let settings = { invoicePrefix: 'INV', receiptPrefix: 'RCT', creditNotePrefix: 'CN', paymentMethods: ['Cash', 'Card', 'UpiDirect'], discountReasons: [] as string[], maxDiscountPercent: 10, paymentLinkExpiryMinutes: 60, version: 0 };

  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: billingOwner(country, flags) }));
  await page.route('**/v1/notifications/inbox?*', r => r.fulfill({ json: { unread: 0, items: [] } }));
  await page.route('**/v1/settings/locations', r => r.fulfill({ json: [{ id: 'f-1', name: 'Adyar', address: null, timeZone: country === 'IN' ? 'Asia/Kolkata' : 'Asia/Dubai' }] }));
  await page.route('**/v1/settings/doctors', r => r.fulfill({
    json: { doctors: [{ id: 'dr-1', fullName: 'Dr. Arjun Rao (synthetic)', facilityId: 'f-1', facilityName: 'Adyar', status: 'Active' }], defaults: { newVisitFee: 50_000, followUpFee: 30_000, currency }, authorities: [] },
  }));
  await page.route('**/v1/settings/services?*', r => r.fulfill({ json: services }));
  await page.route('**/v1/settings/services', r => {
    const body = r.request().postDataJSON() as Record<string, unknown>;
    posts.push({ path: 'services', body });
    if (body.sac === '999999') return r.fulfill({ status: 400, json: { errors: { sac: ['format'] } } });
    const created: Service = { ...body, id: `svc-${services.length}`, name: String(body.name), archivedAt: null, clinicPriceMinor: null, currency, version: 1 };
    services.push(created);
    prices[created.id] = [];
    return r.fulfill({ status: 201, json: created });
  });
  await page.route('**/v1/settings/services/*/prices', r => {
    const id = r.request().url().split('/').at(-2)!;
    if (r.request().method() === 'POST') {
      const body = r.request().postDataJSON() as { doctorId: string | null; amountMinor: number };
      posts.push({ path: `services/${id}/prices`, body });
      const version = { id: `pv-${Date.now()}`, ...body, currency, effectiveFrom: new Date().toISOString(), createdAt: new Date().toISOString() };
      prices[id] = [...(prices[id] ?? []), version];
      const service = services.find(s => s.id === id);
      if (service && body.doctorId === null) service.clinicPriceMinor = body.amountMinor;
      return r.fulfill({ status: 201, json: version });
    }
    return r.fulfill({ json: prices[id] ?? [] });
  });
  await page.route('**/v1/settings/billing', r => {
    if (r.request().method() === 'PUT') {
      const body = r.request().postDataJSON() as typeof settings;
      posts.push({ path: 'billing', body });
      if (body.invoicePrefix.length > 3) return r.fulfill({ status: 400, json: { errors: { invoicePrefix: ['format'] } } });
      if (body.paymentMethods.includes('RazorpayLink') && !razorpay.connected) return r.fulfill({ status: 400, json: { errors: { paymentMethods: ['razorpay_not_connected'] } } });
      if (body.version !== settings.version) return r.fulfill({ status: 409, json: { code: 'changed' } });
      settings = { ...body, version: settings.version + 1 };
    }
    return r.fulfill({ json: settings });
  });
  await page.route('**/v1/settings/billing/razorpay', r => {
    if (r.request().method() === 'PUT') {
      const body = r.request().postDataJSON() as { keyId: string; keySecret: string; webhookSecret: string };
      posts.push({ path: 'razorpay', body });
      razorpay = { connected: true, mode: body.keyId.startsWith('rzp_live_') ? 'Live' : 'Test', keyIdEnding: body.keyId.slice(-4), webhookPath: `/v1/webhooks/razorpay/${'a'.repeat(64)}` };
    }
    return r.fulfill({ json: razorpay });
  });
  return { services, posts };
}

// MDV-P4-004: one bill from the board's Billing card, with the API mocked in the server's shapes (InvoiceView, InvoiceRow, PaymentLinkView). The mock
// keeps the server's rules that the screen shows (totals, balance, over-collection, paid only when the provider says so); `provider.paid` stands for
// Razorpay showing the link paid with the full amount. Synthetic patient and clinic only.
export const billingDesk = {
  user: { id: 'u-joseph', displayName: 'A. Joseph (synthetic)' },
  memberships: [{ id: 'm-1', tenantId: 't-1', tenantName: 'Iyer Family Clinic', facilityId: 'f-1', facilityName: 'Iyer Family Clinic · Adyar', role: 'Receptionist' }],
  active: {
    membershipId: 'm-1', tenantId: 't-1', facilityId: 'f-1', role: 'Receptionist', country: 'IN', flags: ['billing.invoices'], timeZone: 'Asia/Kolkata',
    capabilities: { RegisterBookPatients: 'Full', CheckInQueue: 'Full', InvoicesPayments: 'Full' },
  },
};

export async function mockInvoices(page: Page, { locale = 'en', methods = ['Cash', 'Card', 'UpiDirect', 'RazorpayLink'] }: { locale?: string; methods?: string[] } = {}) {
  const bill = {
    id: 'inv-1', state: 'Draft', number: null as string | null, issuedAt: null as string | null,
    patient: { id: 'p-1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', sexAtBirth: 'Female' },
    appointmentId: 'a5', doctorId: 'dr-1', currency: 'INR',
    lines: [{ id: 'ln-1', serviceId: 'svc-fu', priceVersionId: 'pv-fu', description: 'Consultation (follow-up)', sac: '999312', quantity: 1, unitMinor: 30_000,
      taxTreatment: 'Exempt', gstRateBasisPoints: null, taxMinor: 0, amountMinor: 30_000, suggested: true }],
    subtotalMinor: 30_000, discountMinor: 0, discountReason: null as string | null, taxMinor: 0, roundOffMinor: 0, totalMinor: 30_000, paidMinor: 0, balanceMinor: 0,
    payments: [] as Record<string, unknown>[], cancelReason: null, cancelledAt: null, createdAt: '2026-10-19T05:00:00Z',
    options: { paymentMethods: methods, discountReasons: ['Senior citizen'], maxDiscountPercent: 10, mayExceedMaxDiscount: false }, version: 1,
  };
  const provider = { paid: false };
  const posts: { path: string; body: unknown }[] = [];
  const settle = () => {
    bill.paidMinor = bill.payments.filter(p => p.state === 'Succeeded').reduce((n, p) => n + (p.amountMinor as number), 0);
    bill.balanceMinor = bill.totalMinor - bill.paidMinor;
    bill.state = bill.paidMinor === 0 ? 'Issued' : bill.paidMinor < bill.totalMinor ? 'PartlyPaid' : 'Paid';
    bill.version++;
  };
  const answer = () => ({ json: bill });
  await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
  await page.route('**/v1/me/context', r => r.fulfill({ json: billingDesk }));
  await page.route('**/v1/notifications/inbox?*', r => r.fulfill({ json: { unread: 0, items: [] } }));
  await page.route('**/v1/billing/services', r => r.fulfill({ json: [] }));
  await page.route('**/v1/billing/visits/*/invoice', r => r.fulfill(answer()));
  await page.route('**/v1/billing/invoices/search', r => r.fulfill({ json: [{
    id: bill.id, state: bill.state, number: bill.number, patientId: 'p-1', patientName: bill.patient.fullName, mrn: bill.patient.mrn, currency: 'INR',
    totalMinor: bill.totalMinor, paidMinor: bill.paidMinor, awaitingProvider: bill.payments.some(p => p.state === 'Pending'), createdAt: bill.createdAt, issuedAt: bill.issuedAt,
  }] }));
  await page.route('**/v1/billing/invoices/inv-1', r => r.fulfill(answer()));
  await page.route('**/v1/billing/invoices/inv-1/lines/*', r => {
    const body = r.request().postDataJSON() as { quantity: number };
    posts.push({ path: 'line', body });
    Object.assign(bill.lines[0]!, { quantity: body.quantity, amountMinor: 30_000 * body.quantity, suggested: false });
    bill.version++;
    return r.fulfill(answer());
  });
  await page.route('**/v1/billing/invoices/inv-1/issue', r => {
    posts.push({ path: 'issue', body: r.request().postDataJSON() });
    Object.assign(bill, { number: 'INV/26-27/00001', issuedAt: '2026-10-19T05:10:00Z' });
    settle();
    return r.fulfill(answer());
  });
  await page.route('**/v1/billing/invoices/inv-1/payments', r => {
    const body = r.request().postDataJSON() as { method: string; amountMinor: number; reference: string | null };
    posts.push({ path: 'payments', body });
    if (body.amountMinor > bill.balanceMinor) return r.fulfill({ status: 400, json: { errors: { amountMinor: ['over_collected'] } } });
    bill.payments.push({ id: `pay-${bill.payments.length}`, ...body, state: 'Succeeded', linkUrl: null, recordedAt: '2026-10-19T05:12:00Z', confirmedAt: '2026-10-19T05:12:00Z' });
    settle();
    return r.fulfill(answer());
  });
  await page.route('**/v1/billing/invoices/inv-1/payment-link', r => {
    posts.push({ path: 'payment-link', body: null });
    const id = `pay-${bill.payments.length}`;
    bill.payments.push({ id, method: 'RazorpayLink', amountMinor: bill.balanceMinor, state: 'Pending', reference: null, linkUrl: 'https://rzp.io/i/synthetic', recordedAt: '2026-10-19T05:12:00Z', confirmedAt: null });
    bill.version++;
    return r.fulfill({ json: { invoice: bill, paymentId: id, shortUrl: 'https://rzp.io/i/synthetic' } });
  });
  await page.route('**/v1/billing/invoices/inv-1/payments/*/check', r => {
    posts.push({ path: 'check', body: null });
    const pending = bill.payments.find(p => p.state === 'Pending');
    if (pending && provider.paid) { Object.assign(pending, { state: 'Succeeded', linkUrl: null, confirmedAt: '2026-10-19T05:15:00Z' }); settle(); }
    return r.fulfill(answer());
  });
  return { bill, provider, posts };
}
