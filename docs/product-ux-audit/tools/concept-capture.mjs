// Renders the standalone concept at the audit viewports and saves preview screenshots. No network, no backend.
import { createRequire } from 'node:module';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const here = dirname(fileURLToPath(import.meta.url));
const require = createRequire(resolve(here, '../../../tests/e2e/package.json'));
const { chromium } = require('@playwright/test');
const file = 'file://' + resolve(here, '../concepts/medvya-workspace-concept.html');
const out = resolve(here, '../concepts/screenshots');
const browser = await chromium.launch();
const errors = [];
for (const [w, h, touch] of [[1440, 900, false], [1024, 768, true], [768, 1024, true], [375, 812, true]]) {
  const ctx = await browser.newContext({ viewport: { width: w, height: h }, hasTouch: touch, isMobile: touch });
  const page = await ctx.newPage();
  page.on('pageerror', e => errors.push(String(e)));
  await page.goto(file);
  for (const role of ['doctor', 'desk', 'owner']) {
    await page.click(`.demo [data-role=${role}]`);
    await page.screenshot({ path: `${out}/${role}-${w}.png` });
    const ovf = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1);
    if (ovf) errors.push(`${role} ${w}: horizontal overflow`);
  }
  if (w === 1440) {
    await page.click('.demo [data-role=doctor]');
    await page.check('#t-notes'); await page.screenshot({ path: `${out}/doctor-1440-annotated.png` }); await page.uncheck('#t-notes');
    await page.fill('#rx-in', 'Amoxicillin 500 mg'); await page.click('#rx-add');
    await page.locator('#rx-block').scrollIntoViewIfNeeded(); await page.screenshot({ path: `${out}/doctor-1440-allergy-block.png` });
    await page.fill('#rx-in', 'Augmentin 625 Duo'); await page.click('#rx-add'); await page.locator('#rx-free').scrollIntoViewIfNeeded(); await page.screenshot({ path: `${out}/doctor-1440-free-text-ack.png` }); await page.click('#rx-free-no');
    await page.fill('#rx-in', 'Amlodipine 5 mg'); await page.click('#rx-add');
    await page.fill('#f-exam', 'Chest clear. No pedal oedema.'); await page.fill('#f-assess', 'Type 2 diabetes, above target. BP raised.'); await page.fill('#f-plan', 'Add amlodipine. Repeat HbA1c.');
    await page.click('#todo'); await page.click('#resolve'); await page.click('#fu button:nth-child(2)');
    await page.click('#sign'); await page.screenshot({ path: `${out}/doctor-1440-sign-dialog.png` });
    await page.click('#dlg-yes'); await page.screenshot({ path: `${out}/doctor-1440-signed.png` });
    await page.click('.demo [data-role=desk]'); await page.fill('#find', '90000'); await page.screenshot({ path: `${out}/desk-1440-phone-lookup.png` });
    await page.click('[data-ci="Deepak Nair"]'); await page.screenshot({ path: `${out}/desk-1440-confirm-dob.png` }); await page.click('[data-ci="Deepak Nair"]'); await page.screenshot({ path: `${out}/desk-1440-checked-in.png` });
    await page.check('#t-notes'); await page.screenshot({ path: `${out}/desk-1440-annotated.png` });
    await page.click('.demo [data-role=owner]'); await page.screenshot({ path: `${out}/owner-1440-annotated.png` });
  }
  if (w === 768) { await page.click('.demo [data-role=doctor]'); await page.click('#ctx-toggle'); await page.waitForTimeout(300); await page.screenshot({ path: `${out}/doctor-768-chart-open.png` }); }
  await ctx.close();
}
await browser.close();
console.log(errors.length ? errors.join('\n') : 'ok, no page errors or overflow');
