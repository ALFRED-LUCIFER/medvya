// Reviewer A: owner / practice admin walkthrough: settings tabs, staff invite and offboard safeguards, doctors, hours, formulary, displays + queue TV, audit, data.
import { chromium, session, users, origin } from './walkthrough-lib.mjs';
const flat = t => t.replace(/\n+/g, ' | ');
const only = process.argv[2]?.split(',');
const browser = await chromium.launch();
const s = await session(browser, users.admin, 'admin', 'settings');
const { page } = s;
const step = async (n, fn) => { if (only && !only.includes(n)) return; s.log('--- ' + n); try { await fn(); } catch (e) { s.log(`FAILED ${n}: ${String(e).slice(0, 300)}`); await s.shot('failure-' + n).catch(() => {}); s.log('  text: ' + flat(await s.text()).slice(0, 900)); } };
s.log('=== admin landing: ' + page.url() + ' nav: ' + (await page.locator('nav a').allInnerTexts()).join(' / '));

await step('tabs', async () => {
  for (const tab of ['profile', 'locations', 'hours', 'holidays', 'staff', 'doctors', 'nurse', 'medicines', 'displays', 'metrics', 'terms', 'notifications', 'requests', 'policy', 'review', 'audit', 'data']) {
    await s.goto('/settings?tab=' + tab); const t = flat(await s.text());
    s.log(`  [${tab}] docH=${await page.evaluate(() => document.documentElement.scrollHeight)} :: ` + t.slice(t.indexOf('Security') + 9, t.indexOf('Security') + 700));
  }
});
await step('staff-invite', async () => {
  await s.goto('/settings?tab=staff'); s.reset();
  await s.click(page.getByRole('button', { name: 'Add staff member' }).or(page.getByRole('link', { name: 'Add staff member' })).first(), 'Add staff member'); await s.shot('add-staff');
  s.log('  url=' + page.url() + ' form: ' + flat(await s.text()).slice(0, 1500));
  const dlg = page.getByRole('dialog'); const scope = (await dlg.count()) ? dlg : page.locator('#main');
  s.log('  inputs: ' + (await scope.locator('input, [role=combobox], select, textarea').evaluateAll(a => a.map(x => (x.getAttribute('aria-label') || document.querySelector(`label[for="${x.id}"]`)?.textContent || x.name || x.id) + ':' + (x.type || x.getAttribute('role'))))).join(' ; '));
  const role = scope.getByRole('combobox', { name: /Role/ }).first();
  if (await role.count()) { await role.click(); s.log('  role options: ' + (await page.getByRole('option').allInnerTexts()).join(' / ')); await page.keyboard.press('Escape'); }
  const cancel = scope.getByRole('button', { name: /Cancel|Close/ }).first(); if (await cancel.count()) await cancel.click();
});
await step('offboard-safeguards', async () => {
  await s.goto('/settings?tab=staff');
  for (const who of ['Dr. Arjun Rao', 'K. Lakshmi', 'Priya N.']) {
    const row = page.getByRole('row').filter({ hasText: who }).first();
    const btn = row.getByRole('button', { name: /Offboard/ });
    s.log(`  ${who}: offboard button count=${await btn.count()} enabled=${await btn.isEnabled().catch(() => '-')}`);
    if (await btn.count() && await btn.isEnabled()) { await btn.click(); await s.settle(); await s.shot('offboard-' + who.replace(/\W+/g, '-').toLowerCase());
      const dlg = page.getByRole('dialog').or(page.getByRole('alertdialog')).first();
      s.log('    dialog: ' + flat(await dlg.innerText().catch(async () => (await s.text()).slice(0, 1200))).slice(0, 1300));
      const cancel = page.getByRole('button', { name: /^Cancel$|Keep|Close|Back/ }).first(); if (await cancel.count()) await cancel.click(); else await page.keyboard.press('Escape'); await s.settle(); await s.goto('/settings?tab=staff'); }
  }
  s.log('  can a role be changed in place? row controls: ' + (await page.getByRole('row').filter({ hasText: 'A. Joseph' }).first().locator('button, a, [role=combobox]').allInnerTexts()).join(' / '));
});
await step('doctors-hours-holidays', async () => {
  await s.goto('/settings?tab=doctors'); await s.shot('doctors', true); s.log('  doctors: ' + flat(await s.text()).slice(120, 1700));
  await s.goto('/settings?tab=hours'); await s.shot('hours', true); s.log('  hours: ' + flat(await s.text()).slice(120, 1500));
  await s.goto('/settings?tab=holidays'); await s.shot('holidays', true); s.log('  holidays: ' + flat(await s.text()).slice(120, 900));
  await s.goto('/schedule/leave'); await s.shot('leave', true); s.log('  leave: ' + flat(await s.text()).slice(0, 1200));
});
await step('medicines', async () => {
  await s.goto('/settings?tab=medicines'); await s.shot('medicines', true); const t = flat(await s.text()); s.log('  medicines: ' + t.slice(120, 2600));
  s.log('  count of formulary rows: ' + await page.getByRole('row').count());
});
await step('displays-and-tv', async () => {
  await s.goto('/settings?tab=displays'); s.reset(); await s.shot('displays');
  s.log('  displays: ' + flat(await s.text()).slice(120, 1200));
  const nameBox = page.getByRole('textbox').first();
  await s.fill(nameBox, 'Waiting room TV (reviewer A)', 'display name');
  await s.click(page.getByRole('button', { name: /Add|Create/ }).first(), 'create display'); await s.shot('display-created');
  const t = flat(await s.text()); s.log('  after create: ' + t.slice(120, 1400));
  const link = (await page.locator('#main').innerText()).match(/https?:\/\/\S+|\/app\/\S*queue\S*|\/tv\/\S+|\/queue\S+/);
  const hrefs = await page.locator('#main a[href], #main input').evaluateAll(a => a.map(x => x.getAttribute('href') || x.value).filter(Boolean));
  s.log('  link text match: ' + (link?.[0] ?? 'none') + ' hrefs/inputs: ' + hrefs.join(' ; ').slice(0, 500));
  s.counts('admin: create a waiting-room display');
  let url = link?.[0] ?? hrefs.find(h => /queue|tv|display/i.test(h));
  if (url) { url = url.replace(/^https?:\/\/[^/]+/, origin); if (url.startsWith('/')) url = origin + url;
    const tv = await browser.newContext({ viewport: { width: 1920, height: 1080 } }); const p = await tv.newPage(); await p.clock.setFixedTime(new Date(Date.parse('2026-11-03T04:40:00Z')));
    await p.goto(url); await p.waitForLoadState('networkidle').catch(() => {}); await p.waitForTimeout(1500);
    await p.screenshot({ path: new URL('../screenshots/local-stack/walkthroughs/admin/settings/queue-tv-1920.png', import.meta.url).pathname });
    s.log('  TV url after load: ' + p.url().replace(/[A-Za-z0-9_-]{20,}/g, ':token') + ' TV text: ' + flat(await p.locator('body').innerText()).slice(0, 1200)); await tv.close(); }
});
await step('audit-data', async () => {
  await s.goto('/settings?tab=audit'); await s.shot('audit'); const t = flat(await s.text());
  s.log('  audit rows sample: ' + t.slice(t.indexOf('Outcome'), t.indexOf('Outcome') + 1500));
  const act = page.getByRole('combobox', { name: 'Action' }); await act.click(); s.log('  action filter options: ' + (await page.getByRole('option').allInnerTexts()).join(' / ')); await page.keyboard.press('Escape');
  await s.goto('/settings?tab=data'); await s.shot('data', true); s.log('  data: ' + flat(await s.text()).slice(120, 1500));
  await s.goto('/settings?tab=review'); await s.shot('access-review', true); s.log('  review: ' + flat(await s.text()).slice(120, 1300));
  await s.goto('/settings?tab=policy'); s.log('  policy: ' + flat(await s.text()).slice(120, 1100));
  await s.goto('/settings?tab=metrics'); s.log('  metrics: ' + flat(await s.text()).slice(120, 900));
});
await step('admin-clinical-boundary', async () => {
  await s.goto('/today'); s.log('  admin /today: ' + flat(await s.text()).slice(0, 200));
  await s.goto('/front-desk'); const t = flat(await s.text()); s.log('  admin board actions: ' + (await page.locator('#main button').allInnerTexts()).filter((v, i, a) => a.indexOf(v) === i).join(' / ').slice(0, 300));
  await s.goto('/front-desk/overview'); await s.shot('overview-admin', true); s.log('  overview: ' + flat(await s.text()).slice(0, 900));
});
// restore the default: nurse intake off (the stack is shared with other reviewers)
await step('restore-nurse-off', async () => { await s.goto('/settings?tab=nurse'); const tg = page.getByRole('switch', { name: /^Nurse intake before the doctor/ }); if ((await tg.getAttribute('aria-checked')) === 'true') { await tg.click(); await s.settle(); } s.log('  nurse intake now aria-checked=' + await tg.getAttribute('aria-checked')); });
await browser.close();
