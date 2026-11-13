// Reviewer A, rebuttal check for A-02: does a free-text penicillin-class brand authorise and print for a penicillin-allergic patient?
import { chromium, session, users } from './walkthrough-lib.mjs';
const flat = t => t.replace(/\n+/g, ' | ');
const browser = await chromium.launch();
const s = await session(browser, users.doctor, 'doctor', 'free-text-allergy');
const { page } = s;
try {
  await s.goto('/today');
  const v = page.getByRole('region', { name: "Today's visits" }).getByRole('article', { name: /Anjali Varma/ });
  await s.click(v.getByRole('button', { name: /Start consultation/ }), 'Start consultation (Anjali)');
  const path = new URL(page.url()).pathname;
  const note = page.getByRole('region', { name: 'Consultation note' }); const rx = page.getByRole('region', { name: 'Prescription' });
  const al = note.getByRole('region', { name: 'Allergies' }).or(page.getByRole('region', { name: 'Allergies' })).first();
  await s.fill(page.getByLabel('Allergic to').first(), 'Penicillin', 'allergen'); await s.fill(page.getByLabel('Reaction').first(), 'Rash (synthetic)', 'reaction');
  await s.click(page.getByRole('button', { name: 'Add allergy' }).first(), 'Add allergy');
  s.log('  banner: ' + flat(await page.getByRole('region', { name: 'Patient identity' }).innerText()));
  await s.fill(rx.getByLabel('Medicine', { exact: true }), 'Augmentin 625 Duo', 'free-text brand'); await page.waitForTimeout(1000);
  await s.click(rx.getByRole('button', { name: 'Add as free text' }), 'Add as free text'); await page.waitForTimeout(1200);
  const line = rx.getByRole('row', { name: /Augmentin/ });
  await s.fill(line.getByLabel('Dose', { exact: true }), '1', 'dose');
  for (const [f, o] of [['Unit', 'Tablet'], ['Route', 'By mouth'], ['How often', 'Twice a day (BD)']]) { await line.getByRole('combobox', { name: f, exact: true }).click(); await page.getByRole('option', { name: o, exact: true }).click(); s.c.clicks += 2; }
  await page.waitForTimeout(1500); await rx.scrollIntoViewIfNeeded(); await s.shot('free-text-line-ready');
  s.log('  rx before authorise: ' + flat(await rx.innerText()).slice(0, 1600));
  await s.click(rx.getByRole('button', { name: 'Authorise prescription' }), 'Authorise');
  s.log('  alerts: ' + (await rx.getByRole('alert').allInnerTexts()).join(' / '));
  const c = rx.getByRole('button', { name: 'Confirm identity' });
  if (await c.count()) { await c.click(); await page.getByRole('link', { name: users.doctor }).click(); await page.waitForURL(/\/app\/consult\//); await s.settle(); await s.click(rx.getByRole('button', { name: 'Authorise prescription' }), 'Authorise (after step-up)'); }
  await rx.scrollIntoViewIfNeeded(); await s.shot('after-authorise');
  s.log('  STATUS: ' + (await rx.getByRole('status').allInnerTexts()).join(' / ') + ' || alerts: ' + (await rx.getByRole('alert').allInnerTexts()).join(' / '));
  await page.goto(page.url().split('/app')[0] + path + '/rx/print'); await s.settle(); await s.shot('print', true);
  s.log('  PRINT: ' + flat(await s.text()).slice(0, 900));
  await page.goto(page.url().split('/app')[0] + path); await s.settle();
  await page.getByRole('button', { name: 'End consultation' }).first().click(); await s.settle();
} catch (e) { s.log('FAILED: ' + String(e).slice(0, 400)); await s.shot('failure'); s.log(flat(await s.text()).slice(0, 800)); }
await browser.close();
