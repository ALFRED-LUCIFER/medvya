// Reviewer A: receptionist walkthroughs on the local stack (synthetic data only). node walkthrough-receptionist.mjs [flow,flow]
import { chromium, session, users, synthetic } from './walkthrough-lib.mjs';
const only = process.argv[2]?.split(',');
const want = f => !only || only.includes(f);
const browser = await chromium.launch();
const state = {};
const run = async (flow, fn) => { if (!want(flow)) return; const s = await session(browser, users.receptionist, 'receptionist', flow); s.log(`=== ${flow}`); try { await fn(s); } catch (e) { s.log(`FAILED ${flow}: ${String(e).slice(0, 400)}`); await s.shot('failure').catch(() => {}); s.log('  text at failure: ' + (await s.text()).slice(0, 1500).replace(/\n/g, ' | ')); } await s.close(); };

// 1. Walk-in, new patient: board -> Walk-in -> quick registration -> check in -> token
await run('walkin-new', async s => {
  const { page } = s; const p = state.p = synthetic('Lalitha', 'Raman');
  await s.goto('/front-desk'); s.reset(); await s.shot('board');
  await s.click(page.getByRole('link', { name: 'Walk-in' }), 'Walk-in'); await s.shot('walk-in-empty');
  await s.click(page.getByRole('link', { name: 'New patient: quick registration' }), 'New patient: quick registration');
  await s.fill(page.getByLabel('Full name'), p.name, 'Full name');
  await s.fill(page.getByLabel('Mobile'), p.mobile, 'Mobile');
  await s.fill(page.getByRole('textbox', { name: 'Date of birth', exact: true }), p.dob, 'Date of birth');
  await s.click(page.getByRole('radio', { name: 'Female', exact: true }), 'Sex: Female'); await s.shot('quick-reg-filled');
  await s.click(page.getByRole('button', { name: 'Register patient' }), 'Register patient'); await s.shot('registered');
  s.log('  registered text: ' + (await s.text()).slice(0, 700).replace(/\n/g, ' | '));
  await s.click(page.getByRole('link', { name: 'Check in now (walk-in)' }), 'Check in now (walk-in)'); await s.shot('walk-in-prefilled');
  s.log('  walk-in text: ' + (await s.text()).slice(0, 900).replace(/\n/g, ' | '));
  await s.click(page.getByRole('button', { name: 'Check in now' }), 'Check in now'); await s.shot('checked-in-token');
  s.log('  done text: ' + (await s.text()).slice(0, 900).replace(/\n/g, ' | '));
  s.log('  buttons on success: ' + (await page.locator('#main a, #main button').allInnerTexts()).join(' / '));
  s.counts('walk-in new patient (board -> token)');
  await s.click(page.getByRole('link', { name: 'Back to the board' }).first(), 'Back to the board'); await s.shot('board-after');
  const card = page.getByRole('article', { name: p.name });
  s.log('  card: ' + (await card.innerText()).replace(/\n/g, ' | '));
  s.log('  card actions: ' + (await card.getByRole('button').allInnerTexts()).join(' / '));
});

// 2. Repeat patient found by the last digits of the phone
await run('repeat-by-phone', async s => {
  const { page } = s;
  await s.goto('/front-desk'); s.reset();
  await s.click(page.getByRole('link', { name: 'Walk-in' }), 'Walk-in');
  const box = page.getByRole('searchbox', { name: 'Name, MRN, phone or ID' });
  for (const q of ['01', '0102', '9000000102', '90000']) {
    await box.fill(q); await page.waitForTimeout(1200);
    const txt = (await s.text()).split('New patient')[0];
    s.log(`  search "${q}" (no Search click): ` + txt.slice(txt.indexOf('Search')).slice(0, 500).replace(/\n/g, ' | '));
    if (q === '0102') await s.shot('search-4-digits');
    if (q === '90000') await s.shot('search-shared-prefix');
  }
  await s.fill(box, '0102', 'last 4 digits of phone'); await page.waitForTimeout(1200);
  const hit = page.getByRole('button', { name: /^Deepak Nair/ }).first();
  s.log('  result label: ' + await hit.getAttribute('aria-label').catch(() => null) + ' / ' + (await hit.innerText().catch(() => '')));
  await s.click(hit, 'pick Deepak Nair'); await s.shot('picked');
  s.log('  after pick: ' + (await s.text()).slice(0, 700).replace(/\n/g, ' | '));
  await s.click(page.getByRole('button', { name: 'Check in now' }), 'Check in now'); await s.shot('checked-in');
  s.log('  done text: ' + (await s.text()).slice(0, 500).replace(/\n/g, ' | '));
  s.counts('repeat patient by phone digits (board -> token)');
  // again: same patient the same day
  await s.goto('/front-desk/walk-in'); await box.fill('0102'); await page.waitForTimeout(1200);
  await page.getByRole('button', { name: /^Deepak Nair/ }).first().click(); await page.getByRole('button', { name: 'Check in now' }).click(); await s.settle();
  await s.shot('second-visit-same-day-warning');
  s.log('  second check-in same day: ' + (await page.getByRole('alert').allInnerTexts()).join(' / '));
});

// 3. Duplicate prevention: same name + date of birth as the patient of flow 1 (or Deepak Nair), another phone
await run('duplicate', async s => {
  const { page } = s; const p = state.p ?? { name: 'Deepak Nair (synthetic)', dob: '1979-05-20' };
  await s.goto('/patients/new?mode=quick'); s.reset();
  await s.fill(page.getByLabel('Full name'), p.name, 'same name');
  await s.fill(page.getByLabel('Mobile'), '+91 90000 05555', 'different mobile');
  await s.fill(page.getByRole('textbox', { name: 'Date of birth', exact: true }), p.dob, 'same DOB');
  await s.click(page.getByRole('radio', { name: 'Female', exact: true }), 'sex'); await page.waitForTimeout(1500); await s.shot('duplicate-warning');
  s.log('  page: ' + (await s.text()).slice(0, 1600).replace(/\n/g, ' | '));
  s.log('  buttons: ' + (await page.locator('#main button').allInnerTexts()).join(' / '));
  // near-duplicate: same phone, different spelling, same DOB
  await page.getByLabel('Full name').fill(p.name.replace('Lalitha', 'Lalita').replace('Deepak', 'Dipak')); await page.getByLabel('Mobile').fill(p.mobile ?? '+91 90000 00102'); await page.waitForTimeout(1500);
  await s.shot('near-duplicate-spelling-same-phone');
  s.log('  near-duplicate (spelling differs, same phone+DOB): ' + ((await page.getByRole('region', { name: /possible existing record/ }).innerText().catch(() => 'NO WARNING SHOWN'))).slice(0, 400).replace(/\n/g, ' | '));
  s.log('  save button: ' + (await page.locator('#main button[type=submit], #main button').last().innerText()));
});

// 4. Book on the schedule, then reschedule, then cancel; print day sheet
await run('book-reschedule-cancel', async s => {
  const { page } = s;
  await s.goto('/front-desk'); s.reset();
  await s.click(page.getByRole('link', { name: 'Schedule' }).first(), 'Schedule'); await s.shot('schedule');
  const col = page.getByRole('list', { name: /Dr\. Arjun Rao/ });
  const slots = await col.getByRole('button', { name: /^Book / }).evaluateAll(b => b.map(x => x.getAttribute('aria-label')));
  s.log('  free slots today (Rao): ' + slots.length + ' first: ' + slots.slice(0, 3).join(', '));
  await s.click(col.getByRole('button', { name: /^Book / }).last(), 'last free slot today');
  const panel = page.getByRole('complementary', { name: 'Book appointment' });
  await panel.getByText('Held for you until').waitFor({ timeout: 8000 }).catch(() => {});
  await s.shot('book-panel'); s.log('  panel: ' + (await panel.innerText()).slice(0, 900).replace(/\n/g, ' | '));
  await s.fill(panel.getByRole('searchbox', { name: 'Name, MRN, phone or ID' }), '0101', 'phone last 4'); await page.waitForTimeout(1200);
  await s.click(panel.getByRole('button', { name: /^Meera Iyer/ }).first(), 'pick Meera');
  await s.shot('book-panel-patient'); s.log('  panel after pick: ' + (await panel.innerText()).slice(0, 1200).replace(/\n/g, ' | '));
  const confirm = panel.getByRole('button', { name: 'Confirm booking' });
  s.log('  confirm enabled without reason? ' + await confirm.isEnabled());
  if (!(await confirm.isEnabled())) await s.pick(panel.getByRole('combobox', { name: 'Reason category' }), 'General consultation', 'reason');
  await s.click(confirm, 'Confirm booking'); await s.shot('booked');
  s.log('  booked: ' + (await panel.innerText()).slice(0, 600).replace(/\n/g, ' | '));
  s.counts('book appointment (board -> booked)');
  await s.click(panel.getByRole('button', { name: 'Done' }), 'Done'); s.reset();
  await s.click(col.getByRole('button', { name: /^Meera Iyer \(synthetic\) Booked/ }).last(), 'open the booked visit');
  const visit = page.getByRole('complementary', { name: 'Appointment' });
  await s.shot('appointment-panel'); s.log('  appointment panel: ' + (await visit.innerText()).slice(0, 900).replace(/\n/g, ' | '));
  await s.click(visit.getByRole('button', { name: 'Reschedule' }), 'Reschedule'); await s.shot('reschedule-start');
  s.log('  reschedule panel: ' + (await visit.innerText()).slice(0, 1200).replace(/\n/g, ' | '));
  s.log('  main during reschedule: ' + (await s.text()).slice(0, 500).replace(/\n/g, ' | '));
  state.rescheduleOpen = true;
});
await browser.close();
