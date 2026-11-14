import { expect, test } from '@playwright/test';
import { axe } from './support/desk';
import { commitVisible, fitsOneScreen, listScreens, scrollsInsidePanel, taskScreens } from './support/oneScreen';
import { obscuredWhenTabbing } from './support/pinned';

// MDV-P2-064: one screen per task (DESIGN.md "One screen per task"). At 1440 by 900 the page scrolls at most a quarter of a screen and the
// commit action is on screen unscrolled (AC1); at 1024 by 768 the commit action is still on screen (AC2); long lists scroll inside their
// panel while the page stays still (AC3); no field is under the bottom bar when tabbed to (AC5); English and Arabic pass axe (AC6).
// CI has no API: each screen's endpoints are faked in the browser with the synthetic cast (support/oneScreen.ts).
for (const locale of ['en', 'ar']) {
  for (const s of taskScreens) {
    test(`${locale} ${s.name} fits one 1440x900 screen with its commit action in view, and passes axe`, async ({ page }) => {
      await page.setViewportSize({ width: 1440, height: 900 });
      await s.open(page, locale);
      await fitsOneScreen(page, s.commit?.(page, locale));
      expect(await axe(page)).toEqual([]);
    });
    if (s.commit) test(`${locale} ${s.name} keeps its commit action in view at 1024x768`, async ({ page }) => {
      await page.setViewportSize({ width: 1024, height: 768 });
      await s.open(page, locale);
      await commitVisible(page, s.commit!(page, locale));
    });
  }
  for (const s of listScreens) {
    test(`${locale} a long ${s.name} scrolls inside its panel while the page stays still`, async ({ page }) => {
      await page.setViewportSize({ width: 1440, height: 900 });
      await s.open(page, locale);
      await fitsOneScreen(page);
      await scrollsInsidePanel(page, s.panel(page, locale));
      expect(await axe(page)).toEqual([]);
    });
  }
}

for (const name of ['nurse intake', 'settings profile', 'waitlist']) {
  for (const size of [{ width: 1440, height: 900 }, { width: 1024, height: 768 }]) {
    test(`tabbing through ${name} at ${size.width}x${size.height} leaves no field under the bottom bar`, async ({ page }) => {
      test.setTimeout(90_000);
      await page.setViewportSize(size);
      await taskScreens.find(s => s.name === name)!.open(page, 'en');
      expect(await obscuredWhenTabbing(page)).toEqual([]);
    });
  }
}
