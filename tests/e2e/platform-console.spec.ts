import { createRequire } from 'node:module';
import { expect, test, type Page } from '@playwright/test';

// MDV-P5-040. CI has no console host: its endpoints are mocked with synthetic Medvya staff. The server rules (no access without a verified
// key, a reason on every action, one-hour recovery links, append-only log, no clinic data) are tested on PostgreSQL in PlatformConsoleTests.cs.
// The browser side here is real: Chromium's virtual authenticator answers the WebAuthn prompt.
test.use({ baseURL: 'http://localhost:4175' });

const axePath = createRequire(import.meta.url).resolve('axe-core/axe.min.js');
async function axe(page: Page) {
  await page.addScriptTag({ path: axePath });
  return page.evaluate(async () =>
    (await (window as unknown as { axe: { run: () => Promise<{ violations: { id: string; nodes: unknown[] }[] }> } }).axe.run()).violations
      .map(v => `${v.id} (${v.nodes.length})`));
}

const b64 = (s: string) => Buffer.from(s).toString('base64url');
const owner = (over: object) => ({ displayName: 'Ops Admin (synthetic)', role: 'Owner', keyVerified: false, hasKey: false, canRegister: true, ...over });
const team = {
  lastAccessReviewAt: null, accessReviewDueAt: '2026-11-01T00:00:00Z',
  members: [{ id: 'm-1', displayName: 'Ops Admin (synthetic)', email: 'ops.admin@medvya.example', role: 'Owner', hasKey: true, joined: true, lastActiveAt: '2026-11-07T05:00:00Z', accessEndsOn: null }],
};
const audit = [
  { id: 'a2', occurredAt: '2026-11-07T05:01:00Z', action: 'team.invite', resourceType: 'platform_member', resourceId: 'm-2', subjectTenantId: null, actor: 'Ops Admin (synthetic)', reason: 'New licence reviewer joining' },
  { id: 'a1', occurredAt: '2026-11-07T05:00:00Z', action: 'auth.signin', resourceType: 'platform_session', resourceId: 's-1', subjectTenantId: null, actor: 'Ops Admin (synthetic)', reason: null },
];

async function virtualKey(page: Page) {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('WebAuthn.enable');
  await cdp.send('WebAuthn.addVirtualAuthenticator', { options: { protocol: 'ctap2', transport: 'usb', hasResidentKey: false, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true } });
}

test('a link holder signs on, registers a security key in the browser, and only then reaches the console', async ({ page }) => {
  await virtualKey(page);
  let me: object | null = owner({});
  await page.route('**/v1/me', r => (me ? r.fulfill({ json: me }) : r.fulfill({ status: 401 })));
  await page.route('**/bff/key/registration/options', r => r.fulfill({ json: {
    rp: { id: 'localhost', name: 'Medvya Platform' }, user: { id: b64('member-1'), name: 'ops.admin@medvya.example', displayName: 'Ops Admin (synthetic)' },
    challenge: b64('a-challenge-of-sixteen-plus-bytes'), pubKeyCredParams: [{ type: 'public-key', alg: -7 }], timeout: 60000, attestation: 'none',
    authenticatorSelection: { authenticatorAttachment: 'cross-platform', residentKey: 'discouraged', userVerification: 'required' }, excludeCredentials: [],
  } }));
  let sent: { type?: string; response?: { attestationObject?: string } } = {};
  await page.route('**/bff/key/registration', r => { sent = r.request().postDataJSON(); me = owner({ keyVerified: true, hasKey: true, canRegister: false }); return r.fulfill({ status: 204 }); });

  await page.goto('/');
  await expect(page.getByText('Signed in as Ops Admin (synthetic) · Platform owner')).toBeVisible();
  await expect(page.getByRole('navigation')).toHaveCount(0); // no console until the key step
  expect(await axe(page)).toEqual([]);
  await page.getByRole('button', { name: 'Register security key' }).click();
  await expect(page.getByRole('heading', { name: 'Platform overview' })).toBeVisible();
  expect(sent.type).toBe('public-key');
  expect(sent.response?.attestationObject).toBeTruthy(); // a real authenticator answer, not a stub
});

test('signed out, the console offers company sign-on and explains lost-key recovery', async ({ page }) => {
  await page.route('**/v1/me', r => r.fulfill({ status: 401 }));
  await page.goto('/?signin=failed');
  await expect(page.getByRole('button', { name: 'Continue with company sign-on' })).toBeVisible();
  await expect(page.getByRole('alert')).toContainText('Sign-on didn’t work');
  await expect(page.getByRole('heading', { name: 'Lost your security key?' })).toBeVisible();
  await expect(page.getByText('You can’t see patient records from this console.')).toBeVisible();
});

test('team actions need a reason, which is sent with the invitation; the link is shown once', async ({ page }) => {
  await page.route('**/v1/me', r => r.fulfill({ json: owner({ keyVerified: true, hasKey: true, canRegister: false }) }));
  await page.route('**/v1/team', r => r.fulfill({ json: team }));
  let body: Record<string, unknown> = {};
  await page.route('**/v1/team/invitations', r => { body = r.request().postDataJSON(); return r.fulfill({ status: 201, json: { memberId: 'm-2', token: 'tok_abcdefghijklmnopqrstuvwxyz', expiresAt: '2026-11-07T06:00:00Z' } }); });
  await page.goto('/team');
  await expect(page.getByRole('heading', { name: 'Medvya team access' })).toBeVisible();
  await expect(page.getByText('The quarterly access review is due.')).toBeVisible();
  await page.getByRole('button', { name: 'Invite' }).click();
  await page.getByLabel('Name').fill('Kavya (synthetic)');
  await page.getByLabel('Work email').fill('kavya@medvya.example');
  await page.getByRole('button', { name: 'Create invitation link' }).click();
  // MDV-P2-058: the refused invitation focuses the error summary; its link focuses the reason.
  const summary = page.getByRole('group', { name: 'There is a problem' });
  await expect(summary).toBeFocused();
  await expect(summary.getByRole('link')).toHaveText(['Reason (saved in the audit log): Give a reason of at least 5 characters.']);
  expect(await axe(page)).toEqual([]);
  await summary.getByRole('link').click();
  await expect(page.getByLabel('Reason (saved in the audit log)')).toBeFocused();
  expect(body).toEqual({});
  await page.getByLabel('Reason (saved in the audit log)').fill('New licence reviewer joining');
  await page.getByRole('button', { name: 'Create invitation link' }).click();
  await expect(page.getByRole('heading', { name: 'Invitation link' })).toBeVisible();
  await expect(page.getByText('http://localhost:4175/enrol#link=tok_abcdefghijklmnopqrstuvwxyz')).toBeVisible();
  expect(body).toMatchObject({ displayName: 'Kavya (synthetic)', reason: 'New licence reviewer joining', accessEndsOn: null });
  expect(await axe(page)).toEqual([]);
});

test('the audit log reads in Arabic, right to left, with actions in words', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('medvya.locale', 'ar'));
  await page.route('**/v1/me', r => r.fulfill({ json: owner({ keyVerified: true, hasKey: true, canRegister: false }) }));
  await page.route('**/v1/audit**', r => r.fulfill({ json: audit }));
  await page.goto('/audit');
  await expect(page.getByRole('heading', { name: 'سجل تدقيق المنصة' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.dir)).toBe('rtl');
  await expect(page.getByRole('row', { name: /دُعي عضو في الفريق/ })).toContainText('New licence reviewer joining');
  await expect(page.getByRole('cell', { name: /team\.|auth\./ })).toHaveCount(0);
  expect(await axe(page)).toEqual([]);
});

test('a reviewer sees approvals but no team or audit pages', async ({ page }) => {
  await page.route('**/v1/me', r => r.fulfill({ json: owner({ role: 'Reviewer', keyVerified: true, hasKey: true, canRegister: false }) }));
  await page.goto('/');
  await expect(page.getByRole('navigation').getByRole('link')).toHaveText(['Overview', 'Approvals']);
});

// MDV-P2-059 AC4: on a phone the sign-in is exactly as wide as the screen, in both directions, and every control is a 44 px target.
for (const locale of ['en', 'ar']) {
  test(`at 375 px the ${locale} sign-in does not scroll sideways and its controls are 44 px`, async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 740 });
    await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
    await page.route('**/v1/me', r => r.fulfill({ status: 401 }));
    await page.goto('/?signin=failed');
    await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(375);
    for (const control of await page.locator('button, select, [role=combobox]').all())
      expect((await control.boundingBox())!.height).toBeGreaterThanOrEqual(44);
    expect(await axe(page)).toEqual([]);
  });
}

// MDV-P2-059 AC1 and AC6: every console page has one h1, in the shared 44 px header row, at the same height, with content from 130 px.
const consolePages = ['/', '/approvals', '/team', '/audit'];
for (const locale of ['en', 'ar']) {
  test(`every ${locale} console page puts its one title in the same 44 px row`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
    await page.route('**/v1/me', r => r.fulfill({ json: owner({ keyVerified: true, hasKey: true, canRegister: false }) }));
    await page.route('**/v1/team', r => r.fulfill({ json: team }));
    await page.route('**/v1/audit**', r => r.fulfill({ json: audit }));
    await page.route('**/v1/organisations/?filter=*', r => r.fulfill({ json: [] }));
    const tops = new Set<number>();
    for (const path of consolePages) {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1);
      const row = (await page.locator('[data-slot=page-header-row]').boundingBox())!;
      expect(row.height, path).toBe(44);
      expect(row.y + row.height + 12, path).toBeLessThanOrEqual(130); // 12 px gap below the header (DESIGN.md Layout)
      tops.add(row.y);
      expect(await axe(page), path).toEqual([]);
    }
    expect([...tops]).toHaveLength(1);
  });
}

// MDV-P2-054 AC5: on a phone the team and audit tables are stacked: each row is a block that starts with who or when, every other field
// sits under its column's name, and nothing scrolls sideways.
const stackedTables = [
  { path: '/team', label: { en: 'Medvya team access', ar: 'وصول فريق Medvya' }, first: 'Ops Admin (synthetic)', fields: { en: ['Role', 'Access ends'], ar: ['الدور', 'ينتهي الوصول'] } },
  { path: '/audit', label: { en: 'Platform audit log', ar: 'سجل تدقيق المنصة' }, first: '2026', fields: { en: ['What', 'Reason'], ar: ['ماذا', 'السبب'] } },
] as const;
for (const locale of ['en', 'ar'] as const) {
  test(`at 375 px the ${locale} team and audit tables are stacked and labelled, and nothing scrolls sideways`, async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 740 });
    await page.addInitScript(l => localStorage.setItem('medvya.locale', l), locale);
    await page.route('**/v1/me', r => r.fulfill({ json: owner({ keyVerified: true, hasKey: true, canRegister: false }) }));
    await page.route('**/v1/team', r => r.fulfill({ json: team }));
    await page.route('**/v1/audit**', r => r.fulfill({ json: audit }));
    for (const t of stackedTables) {
      await page.goto(t.path);
      const region = page.getByRole('region', { name: t.label[locale] });
      await expect(region.getByRole('rowheader').first()).toContainText(t.first);
      const row = region.getByRole('row').nth(1);
      for (const name of t.fields[locale]) await expect(row.getByRole('cell').getByText(name, { exact: true })).toBeVisible(); // the column's name above its value
      expect(await region.evaluate(e => e.scrollWidth - e.clientWidth), t.path).toBeLessThanOrEqual(0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth), t.path).toBeLessThanOrEqual(0);
      expect(await axe(page), t.path).toEqual([]);
    }
  });
}
