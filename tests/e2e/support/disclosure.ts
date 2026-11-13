import type { Page } from '@playwright/test';

/** Orders and Referrals sit in a closed disclosure on the consultation until used (MDV audit P1-04), open already when they hold something: open it by its title if it is closed. */
export const openPart = async (page: Page, title: string) => {
  const summary = page.locator('summary').filter({ hasText: new RegExp(`^${title}$`) });
  if (!(await summary.evaluate(el => (el.parentElement as HTMLDetailsElement).open))) await summary.click();
};
