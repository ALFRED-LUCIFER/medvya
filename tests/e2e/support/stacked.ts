import { expect, type Locator, type Page } from '@playwright/test';
import { axe } from './desk';

/**
 * MDV-P2-054 AC5: at 375 px a table's rows are stacked blocks: the identifying cell first and bold, each other value under its column's
 * name, nothing scrolling sideways, and axe clean. `table` is the table's named region; `labels` are the column names the first row shows.
 */
export async function expectStacked(page: Page, table: Locator, labels: string[]) {
  const row = table.getByRole('row').nth(1); // row 0 is the column headers, read by screen readers only on a phone
  const id = row.getByRole('rowheader');
  await id.scrollIntoViewIfNeeded();
  await expect(id).toBeVisible();
  expect(await id.evaluate(e => Number(getComputedStyle(e).fontWeight))).toBeGreaterThanOrEqual(700);
  for (const label of labels) await expect(row.getByRole('cell').getByText(label, { exact: true })).toBeVisible();
  expect(await table.evaluate(e => e.scrollWidth - e.clientWidth)).toBeLessThanOrEqual(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  expect(await axe(page)).toEqual([]);
}
