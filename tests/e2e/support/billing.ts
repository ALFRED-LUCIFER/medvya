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
      if (body.version !== settings.version) return r.fulfill({ status: 409, json: { code: 'changed' } });
      settings = { ...body, version: settings.version + 1 };
    }
    return r.fulfill({ json: settings });
  });
  return { services, posts };
}
