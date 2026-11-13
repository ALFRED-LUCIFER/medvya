// Reviewer A: receptionist, part 2: reschedule, cancel, no-show, board check-in, call in, undo, reassign, waitlist, day sheet, end of day.
import { chromium, session, users } from './walkthrough-lib.mjs';
const only = process.argv[2]?.split(',');
const browser = await chromium.launch();
const flat = t => t.replace(/\n+/g, ' | ');
const run = async (flow, fn) => { if (only && !only.includes(flow)) return; const s = await session(browser, users.receptionist, 'receptionist', flow); s.log(`=== ${flow}`); try { await fn(s); } catch (e) { s.log(`FAILED ${flow}: ${String(e).slice(0, 400)}`); await s.shot('failure').catch(() => {}); s.log('  text at failure: ' + flat(await s.text()).slice(0, 1500)); } await s.close(); };
const rao = page => page.getByRole('list', { name: /Dr\. Arjun Rao/ });

await run('reschedule-cancel', async s => {
  const { page } = s; await s.goto('/schedule'); s.reset();
  await s.click(rao(page).getByRole('button', { name: /^Meera Iyer \(synthetic\) Booked/ }).last(), 'open booked visit');
  const visit = page.getByRole('complementary', { name: 'Appointment' });
  await s.click(visit.getByRole('button', { name: 'Reschedule' }), 'Reschedule');
  const free = rao(page).getByRole('button', { name: /^(Book|Move) / });
  s.log('  slot buttons in reschedule mode: ' + (await free.evaluateAll(b => b.map(x => x.getAttribute('aria-label')))).slice(0, 6).join(', '));
  s.log('  can pick another day while rescheduling? next-day button enabled=' + await page.getByRole('button', { name: 'Next day' }).isEnabled());
  await s.click(page.getByRole('button', { name: 'Next day' }), 'Next day'); await s.shot('reschedule-next-day');
  s.log('  panel after next day: ' + flat(await visit.innerText().catch(() => 'PANEL GONE')).slice(0, 400));
  const free2 = rao(page).getByRole('button', { name: /^(Book|Move) / });
  s.log('  next-day slots: ' + (await free2.count()));
  await s.click(free2.first(), 'pick first slot next day'); await s.shot('reschedule-confirm');
  s.log('  after picking slot: ' + flat(await page.getByRole('complementary').last().innerText()).slice(0, 900));
  const confirm = page.getByRole('complementary').last().getByRole('button', { name: /Confirm|Move|Reschedule/ }).first();
  if (await confirm.isVisible().catch(() => false)) { s.log('  confirm button: ' + await confirm.innerText()); await s.click(confirm, 'confirm move'); }
  await s.shot('rescheduled'); s.log('  result: ' + flat(await page.getByRole('complementary').last().innerText()).slice(0, 600));
  s.counts('reschedule to next day');
  s.reset();
  const again = rao(page).getByRole('button', { name: /^Meera Iyer \(synthetic\) Booked/ }).last();
  if (!(await page.getByRole('complementary', { name: 'Appointment' }).isVisible().catch(() => false))) await s.click(again, 'open moved visit');
  const v2 = page.getByRole('complementary', { name: 'Appointment' });
  await s.click(v2.getByRole('button', { name: 'Cancel appointment' }), 'Cancel appointment'); await s.shot('cancel-reason');
  s.log('  cancel form: ' + flat(await v2.innerText()).slice(0, 700));
  await v2.getByRole('combobox', { name: 'Reason' }).click(); s.c.clicks++;
  s.log('  cancel reasons: ' + (await page.getByRole('option').allInnerTexts()).join(' / '));
  await page.getByRole('option').first().click(); s.c.clicks++;
  await s.click(v2.getByRole('button', { name: 'Cancel appointment' }), 'confirm cancel'); await s.shot('cancelled');
  s.log('  cancelled: ' + flat(await v2.innerText()).slice(0, 500));
  s.counts('cancel appointment');
});

await run('board-checkin-callin', async s => {
  const { page } = s;
  // book Meera in the next free slot today so there is a Booked card
  await s.goto('/schedule');
  await rao(page).getByRole('button', { name: /^Book / }).first().click();
  const panel = page.getByRole('complementary', { name: 'Book appointment' });
  await panel.getByText('Held for you until').waitFor();
  await panel.getByRole('searchbox').fill('0101'); await page.waitForTimeout(1200);
  await panel.getByRole('button', { name: /^Meera Iyer/ }).first().click(); await panel.getByRole('button', { name: 'Confirm booking' }).click(); await s.settle();
  s.log('  booked: ' + flat(await panel.innerText()).slice(0, 200));
  await s.goto('/front-desk'); s.reset(); await s.shot('board-with-booked');
  const card = page.getByRole('region', { name: /^Booked/ }).getByRole('article', { name: 'Meera Iyer (synthetic)' }).first();
  s.log('  booked card: ' + flat(await card.innerText()));
  await s.click(card.getByRole('button', { name: 'Check in' }), 'Check in'); await s.shot('confirm-identity');
  s.log('  confirm identity: ' + flat(await page.getByRole('group', { name: 'Confirm identity' }).innerText()));
  await s.click(page.getByRole('group', { name: 'Confirm identity' }).getByRole('button', { name: 'Confirm arrival' }), 'Confirm arrival'); await s.shot('checked-in');
  s.counts('check in a booked patient from the board');
  const now = page.getByRole('article', { name: 'Meera Iyer (synthetic)' }).filter({ hasText: 'Waiting' }).first();
  s.log('  card after check-in: ' + flat(await now.innerText()) + ' || actions: ' + (await now.getByRole('button').allInnerTexts()).join(' / '));
  s.log('  is there an undo / cancel check-in / left without being seen on the card? ' + (await now.getByRole('button', { name: /undo|cancel|left|remove|no-show/i }).count()));
  // the card itself: does clicking the name open anything (details, phone, reprint)?
  s.log('  links on card: ' + (await now.getByRole('link').allInnerTexts()).join(' / '));
  await s.click(now.getByRole('button', { name: 'Call in' }), 'Call in'); await s.shot('call-in');
  s.log('  after Call in: ' + flat(await s.text()).slice(0, 200) + ' ... dialogs=' + await page.getByRole('dialog').count());
  const inConsult = page.getByRole('region', { name: /^In consult/ });
  s.log('  In consult column: ' + flat(await inConsult.innerText()).slice(0, 500));
  // what can the desk do with a visit that is In consult / can it be put back?
  s.log('  In consult card actions: ' + (await inConsult.getByRole('button').allInnerTexts()).join(' / '));
  // the same visit from the schedule: cancel / no-show available after check-in?
  await s.goto('/schedule');
  const visitBtn = rao(page).getByRole('button', { name: /^Meera Iyer \(synthetic\) (In room|Checked in)/ }).first();
  if (await visitBtn.count()) { await s.click(visitBtn, 'open in-room visit on schedule'); await s.shot('schedule-inroom-panel');
    s.log('  schedule panel for the in-room visit: ' + flat(await page.getByRole('complementary', { name: 'Appointment' }).innerText()).slice(0, 700)); }
  else s.log('  in-room visit not found on schedule; buttons: ' + (await rao(page).getByRole('button').evaluateAll(b => b.map(x => x.getAttribute('aria-label') || x.textContent))).slice(0, 12).join(' ; '));
  // put it back to done from the board so the doctor's day is not blocked
  await s.goto('/front-desk');
  const done = page.getByRole('region', { name: /^In consult/ }).getByRole('button', { name: /Consult done|Close visit/ }).first();
  if (await done.count()) { await s.click(done, 'Consult done (desk)'); s.log('  desk closed the consult without a doctor: ' + flat(await page.getByRole('region', { name: /^Done/ }).innerText()).slice(0, 300)); await s.shot('desk-closed-consult'); }
});

await run('reassign', async s => {
  const { page } = s; await s.goto('/front-desk'); s.reset();
  await s.click(page.getByRole('group', { name: 'Board views' }).getByRole('link', { name: 'By doctor' }), 'By doctor'); await s.shot('overview');
  const row = page.getByRole('region', { name: /^Waiting room/ }).getByRole('row').filter({ hasText: 'Lalitha Raman' }).first();
  s.log('  row: ' + flat(await row.innerText()));
  await s.click(row.getByRole('button', { name: /^Move to another doctor/ }), 'Move to another doctor'); await s.shot('move-form');
  const move = page.getByRole('form', { name: /^Move / });
  s.log('  form: ' + flat(await move.innerText()));
  await move.getByRole('combobox', { name: 'Move to' }).click(); s.c.clicks++; s.log('  doctors: ' + (await page.getByRole('option').allInnerTexts()).join(' / ')); await page.getByRole('option', { name: /Dr\. Arjun Rao/ }).click(); s.c.clicks++;
  await move.getByRole('combobox', { name: 'Reason' }).click(); s.c.clicks++; s.log('  reasons: ' + (await page.getByRole('option').allInnerTexts()).join(' / ')); await page.getByRole('option').first().click(); s.c.clicks++;
  await s.click(move.getByRole('button', { name: 'Move patient' }), 'Move patient'); await s.shot('moved');
  s.log('  row after: ' + flat(await page.getByRole('region', { name: /^Waiting room/ }).getByRole('row').filter({ hasText: 'Lalitha Raman' }).first().innerText()));
  s.counts('reassign a waiting patient to another doctor');
});

await run('waitlist', async s => {
  const { page } = s; await s.goto('/front-desk'); s.reset();
  await s.click(page.getByRole('link', { name: 'Waitlist' }), 'Waitlist'); await s.shot('waitlist', true);
  s.log('  waitlist: ' + flat(await s.text()).slice(0, 1500));
  await s.fill(page.getByRole('searchbox', { name: 'Name, MRN, phone or ID' }), '0101', 'phone digits'); await page.waitForTimeout(1200);
  await s.click(page.getByRole('button', { name: /^Meera Iyer/ }).first(), 'pick');
  await s.click(page.getByRole('checkbox', { name: 'Wednesday' }), 'Wednesday');
  await s.click(page.getByRole('button', { name: 'Add to waitlist' }), 'Add to waitlist'); await s.shot('waitlist-added', true);
  s.log('  after add: ' + flat(await s.text()).slice(0, 1200));
  s.counts('add to waitlist');
});

await run('day-sheet-end-of-day', async s => {
  const { page } = s; await s.goto('/schedule'); s.reset();
  const popup = page.waitForEvent('popup', { timeout: 4000 }).catch(() => null);
  await s.click(page.getByRole('button', { name: 'Print day sheet' }).or(page.getByRole('link', { name: 'Print day sheet' })).first(), 'Print day sheet');
  const pop = await popup; s.log('  popup: ' + (pop ? pop.url() : 'none') + ' url now: ' + page.url());
  await s.shot('day-sheet', true); s.log('  sheet: ' + flat(await (pop ?? page).locator('body').innerText()).slice(0, 1500));
  await s.goto('/front-desk');
  s.log('  board actions (end of day?): ' + (await page.locator('#main a, #main button').allInnerTexts()).filter((v, i, a) => a.indexOf(v) === i).join(' / ').slice(0, 600));
  await s.goto('/more'); s.log('  More: ' + flat(await s.text()).slice(0, 500));
  // header search dialog: actions offered from a hit
  await s.goto('/front-desk'); await page.getByRole('button', { name: 'Search patients' }).click();
  const dlg = page.getByRole('dialog', { name: 'Search patients' }); await dlg.getByRole('combobox').fill('0101'); await page.waitForTimeout(1200); await s.shot('global-search');
  s.log('  global search: ' + flat(await dlg.innerText()).slice(0, 600));
});
await browser.close();
