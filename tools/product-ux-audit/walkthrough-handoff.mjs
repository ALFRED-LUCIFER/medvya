// Reviewer A: reception -> nurse -> doctor handoff on the local stack. Writes the patient it made to reviews/reviewer-a-evidence/walkthrough-a-state.json for the doctor script.
import { writeFileSync } from 'node:fs';
import { chromium, session, users, synthetic, tablet, layout, root } from './walkthrough-lib.mjs';
const browser = await chromium.launch();
const p = synthetic('Bhavani', 'Subramanian');
const flat = t => t.replace(/\n+/g, ' | ');
const guard = async (s, name, fn) => { try { await fn(); } catch (e) { s.log(`FAILED ${name}: ${String(e).slice(0, 300)}`); await s.shot(`failure-${name}`).catch(() => {}); s.log('  text: ' + flat(await s.text()).slice(0, 1200)); } };

// Admin switches nurse intake on
const admin = await session(browser, users.admin, 'admin', 'nurse-intake-switch');
await guard(admin, 'switch', async () => {
  const { page } = admin; admin.log('=== admin: switch nurse intake on'); admin.log('  landing: ' + page.url());
  admin.reset();
  await admin.click(page.getByRole('link', { name: 'Practice settings' }).first(), 'Practice settings'); await admin.shot('settings-landing');
  admin.log('  tabs: ' + (await page.locator('#main [role=tab], #main nav a').allInnerTexts()).join(' / '));
  await admin.click(page.getByRole('tab', { name: 'Clinical' }).or(page.getByRole('link', { name: 'Clinical' })).first(), 'Clinical');
  admin.log('  sub tabs: ' + (await page.locator('#main [role=tab], #main nav a').allInnerTexts()).join(' / '));
  await admin.click(page.getByRole('tab', { name: 'Nurse intake' }).or(page.getByRole('link', { name: 'Nurse intake' })).first(), 'Nurse intake'); await admin.shot('nurse-tab');
  const toggle = page.getByRole('switch', { name: /^Nurse intake before the doctor/ });
  admin.log('  before: aria-checked=' + await toggle.getAttribute('aria-checked'));
  if ((await toggle.getAttribute('aria-checked')) !== 'true') await admin.click(toggle, 'switch on');
  admin.log('  after: aria-checked=' + await toggle.getAttribute('aria-checked') + ' dialogs=' + await page.getByRole('dialog').count());
  admin.log('  text: ' + flat(await admin.text()).slice(0, 900)); await admin.shot('nurse-on');
  admin.counts('admin switches nurse intake on');
});
await admin.close();

// Receptionist: walk-in, then send to nurse
const desk = await session(browser, users.receptionist, 'handoff', 'reception');
await guard(desk, 'walkin', async () => {
  const { page } = desk; desk.log('=== reception: walk-in ' + p.name);
  await desk.goto('/front-desk'); desk.reset();
  await desk.click(page.getByRole('link', { name: 'Walk-in' }), 'Walk-in');
  await desk.click(page.getByRole('link', { name: 'New patient: quick registration' }), 'quick registration');
  await desk.fill(page.getByLabel('Full name'), p.name, 'name'); await desk.fill(page.getByLabel('Mobile'), p.mobile, 'mobile');
  await desk.fill(page.getByRole('textbox', { name: 'Date of birth', exact: true }), p.dob, 'dob'); await desk.click(page.getByRole('radio', { name: 'Female', exact: true }), 'sex');
  await desk.click(page.getByRole('button', { name: 'Register patient' }), 'Register');
  await desk.click(page.getByRole('link', { name: 'Check in now (walk-in)' }), 'Check in now (walk-in)');
  await desk.pick(page.getByRole('combobox', { name: 'Doctor' }), /Dr\. Arjun Rao/, 'doctor Rao');
  await desk.click(page.getByRole('button', { name: 'Check in now' }), 'Check in now'); await desk.shot('token');
  desk.log('  ' + flat(await desk.text()).slice(0, 500));
  await desk.click(page.getByRole('link', { name: 'Back to the board' }).first(), 'Back to the board');
  const card = page.getByRole('article', { name: p.name });
  desk.log('  card (nurse intake on): ' + flat(await card.innerText()) + ' || column: ' + await card.evaluate(e => e.closest('section,[role=region]')?.getAttribute('aria-label')));
  await desk.shot('board-checked-in');
  await desk.click(card.getByRole('button', { name: 'Send to nurse' }), 'Send to nurse'); await desk.shot('board-with-nurse');
  desk.log('  card after send: ' + flat(await page.getByRole('article', { name: p.name }).innerText()));
  desk.counts('reception walk-in + send to nurse');
});

// Nurse on a tablet (1024x768 touch)
const nurse = await session(browser, users.nurse, 'nurse', 'intake-tablet', tablet);
let intakeUrl = '';
await guard(nurse, 'intake', async () => {
  const { page } = nurse; nurse.log('=== nurse: landing ' + page.url()); nurse.reset(); await nurse.shot('landing-queue');
  nurse.log('  queue: ' + flat(await nurse.text()).slice(0, 900));
  await nurse.click(page.getByRole('link', { name: `Open intake for ${p.name}` }).first(), 'Open intake'); intakeUrl = page.url();
  await nurse.shot('intake-top'); await nurse.shot('intake-full', true);
  nurse.log('  intake text: ' + flat(await nurse.text()).slice(0, 2500));
  nurse.log('  layout: ' + JSON.stringify(await layout(page, { header: 'header', identity: '[aria-label="Patient identity"]', firstInput: '#main input, #main textarea' })) + ' docH=' + await page.evaluate(() => document.documentElement.scrollHeight));
  nurse.log('  identity sticky? position=' + await page.locator('[aria-label="Patient identity"]').evaluate(e => { let n = e; while (n && n !== document.body) { const p = getComputedStyle(n).position; if (p === 'sticky' || p === 'fixed') return p; n = n.parentElement; } return 'static'; }));
  nurse.log('  inputmode of vitals: ' + JSON.stringify(await page.locator('#main input').evaluateAll(a => a.map(i => `${i.getAttribute('aria-label') || i.id}:${i.type}/${i.inputMode || '-'}`))));
  await nurse.fill(page.getByLabel("Chief complaint, in the patient's words"), 'Fever and sore throat for three days (synthetic)', 'complaint');
  await nurse.fill(page.getByLabel('BP systolic'), '90', 'BP sys (deliberately reversed)'); await nurse.fill(page.getByLabel('BP diastolic'), '168', 'BP dia');
  await nurse.click(page.getByRole('button', { name: 'Save vitals' }), 'Save vitals'); await nurse.shot('bp-reversed-error');
  nurse.log('  reversed BP: ' + flat((await page.locator('#main [role=alert], #main [aria-invalid=true] ~ *').allInnerTexts()).join(' / ')).slice(0, 300));
  await page.getByLabel('BP systolic').fill('168'); await page.getByLabel('BP diastolic').fill('96');
  await nurse.fill(page.getByLabel('Pulse'), '104', 'pulse'); await nurse.fill(page.getByLabel('Temperature'), '39.2', 'temp'); await nurse.fill(page.getByLabel('SpO₂'), '97', 'spo2'); await nurse.fill(page.getByLabel('Weight'), '62', 'weight');
  await nurse.click(page.getByRole('button', { name: 'Save vitals' }), 'Save vitals'); await nurse.shot('vitals-flagged');
  nurse.log('  after save: ' + flat(await nurse.text()).slice(0, 1800));
  for (const b of await page.getByRole('button', { name: /^Keep this value:/ }).all()) { await nurse.click(page.getByRole('button', { name: /^Keep this value:/ }).first(), 'Keep flagged value'); }
  const al = page.getByRole('region', { name: 'Allergies' });
  await nurse.fill(al.getByLabel('Allergic to'), 'Penicillin', 'allergen'); await nurse.fill(al.getByLabel('Reaction'), 'Rash (synthetic)', 'reaction');
  await nurse.click(al.getByRole('button', { name: 'Add allergy' }), 'Add allergy'); await nurse.shot('allergy-added');
  nurse.log('  allergies: ' + flat(await al.innerText()).slice(0, 500));
  const med = page.getByRole('region', { name: 'Medicines the patient takes' });
  nurse.log('  medicines region: ' + flat(await med.innerText().catch(() => 'absent')).slice(0, 300));
  await nurse.click(page.getByRole('button', { name: 'Escalate to doctor' }), 'Escalate'); await nurse.fill(page.getByRole('textbox', { name: 'Reason for the doctor' }), 'High fever with BP 168/96 (synthetic)', 'reason');
  await nurse.click(page.getByRole('button', { name: 'Send urgent item' }), 'Send urgent item'); await nurse.shot('escalated');
  nurse.counts('nurse intake up to hand-off (tablet)');
});

// Two people on the same visit: the desk presses "Intake done" on the board while the nurse still has the intake open
await guard(desk, 'concurrent', async () => {
  const { page } = desk; await desk.goto('/front-desk');
  const card = page.getByRole('article', { name: p.name });
  desk.log('=== concurrency: desk card actions while nurse works: ' + (await card.getByRole('button').allInnerTexts()).join(' / ') + ' links: ' + (await card.getByRole('link').allInnerTexts()).join(' / '));
  await desk.shot('card-while-nurse-works');
});
await guard(nurse, 'handoff', async () => {
  const { page } = nurse; nurse.reset();
  const hand = page.getByRole('button', { name: 'Hand off · Ready for doctor' });
  nurse.log('  hand-off enabled=' + await hand.isEnabled());
  await nurse.click(hand, 'Hand off'); await nurse.shot('after-handoff');
  nurse.log('  after hand-off url=' + page.url() + ' text: ' + flat(await nurse.text()).slice(0, 400));
  // can the nurse reopen and correct a vital after hand-off?
  await page.goto(intakeUrl); await nurse.settle(); await nurse.shot('intake-reopened-after-handoff');
  nurse.log('  reopened intake: ' + flat(await nurse.text()).slice(0, 700));
  nurse.counts('nurse hand-off');
});
await nurse.close(); await desk.close();
writeFileSync(`${root}/reviews/reviewer-a-evidence/walkthrough-a-state.json`, JSON.stringify({ patient: p, intakeUrl }, null, 1));
await browser.close();
