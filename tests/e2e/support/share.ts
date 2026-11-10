import type { Page } from '@playwright/test';

// MDV-P2-020: the shares of the open consultation's prescription (RxShareView) and the public link endpoints, in the server's shapes.
// The gates (authorised and unchanged only, the code to the registered mobile, expiry, audit) are proven on PostgreSQL in RxShareTests.
type Share = {
  id: string; recipient: 'Patient' | 'Pharmacy'; channel: 'Sms' | 'Email'; pharmacyName: string | null; versionNo: number; replaced: boolean;
  sharedBy: string; sharedAt: string; expiresAt: string;
  dispatch: { state: string; pharmacyRef: string | null; dispensedAt: string | null; changedBy: string; changedAt: string } | null;
};

export async function mockShares(page: Page) {
  const shares: Share[] = [];
  const calls = { shared: [] as unknown[], dispatched: [] as unknown[] };
  await page.route('**/v1/encounters/e-1/prescription/shares', r => {
    if (r.request().method() === 'GET') return r.fulfill({ json: shares });
    const body = r.request().postDataJSON() as { recipient: 'Patient' | 'Pharmacy'; channel?: 'Sms' | 'Email'; pharmacyName?: string };
    calls.shared.push(body);
    const pharmacy = body.recipient === 'Pharmacy';
    shares.unshift({
      id: `s-${shares.length + 1}`, recipient: body.recipient, channel: body.channel ?? 'Email', pharmacyName: body.pharmacyName ?? null, versionNo: 1, replaced: false,
      sharedBy: 'u-rao', sharedAt: '2026-10-19T05:00:00Z', expiresAt: '2026-10-22T05:00:00Z',
      dispatch: pharmacy ? { state: 'Sent', pharmacyRef: null, dispensedAt: null, changedBy: 'u-rao', changedAt: '2026-10-19T05:00:00Z' } : null,
    });
    return r.fulfill({ json: shares });
  });
  await page.route('**/v1/prescription-shares/*/dispatch', r => {
    const id = new URL(r.request().url()).pathname.split('/')[3]!;
    const body = r.request().postDataJSON() as { state: string; pharmacyRef?: string | null; dispensedAt?: string };
    calls.dispatched.push(body);
    const share = shares.find(s => s.id === id)!;
    share.dispatch = { state: body.state, pharmacyRef: body.pharmacyRef ?? null, dispensedAt: body.dispensedAt ?? null, changedBy: 'u-rao', changedAt: '2026-10-19T06:00:00Z' };
    return r.fulfill({ json: shares });
  });
  return { shares, calls };
}

/** The public link: "synthetic-link" is live with code 482913; "replaced-link" was replaced; anything else is unknown or expired (404). */
export async function mockSharedLink(page: Page) {
  const bodies: unknown[] = [];
  await page.route('**/v1/prescriptions/shared/code', r => {
    const { token } = r.request().postDataJSON() as { token: string };
    bodies.push({ token });
    if (token === 'replaced-link') return r.fulfill({ status: 410, json: { code: 'replaced' } });
    return token === 'synthetic-link' ? r.fulfill({ status: 204 }) : r.fulfill({ status: 404, json: {} });
  });
  await page.route('**/v1/prescriptions/shared/open', r => {
    const body = r.request().postDataJSON() as { token: string; code: string };
    bodies.push(body);
    if (body.token !== 'synthetic-link') return r.fulfill({ status: 404, json: {} });
    return body.code === '482913'
      ? r.fulfill({ status: 200, contentType: 'application/pdf', body: '%PDF-1.4 synthetic' })
      : r.fulfill({ status: 403, json: { code: 'wrong_code' } });
  });
  return bodies;
}
