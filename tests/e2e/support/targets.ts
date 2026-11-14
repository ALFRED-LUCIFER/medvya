import type { Page } from '@playwright/test';

// MDV-P2-062 (DESIGN.md, Density): the sizes follow the pointer. With a mouse a control is 36 px, a small button 30 px and a row 40 px;
// under any-pointer: coarse all three are 44, 44 and 48 px. The `touch` project (playwright.config.ts) reruns every "control-size targets"
// test with touch, so each area proves both.
const coarse = (page: Page) => page.evaluate(() => matchMedia('(any-pointer: coarse)').matches);

/** A library control (input, select, button, view switch): 36 px with a mouse, 44 px on touch. */
export const controlPx = async (page: Page) => ((await coarse(page)) ? 44 : 36);

/** The smallest target of any kind, small buttons included: 30 px with a mouse, 44 px on touch (WCAG 2.5.8 floor is 24). */
export const targetPx = async (page: Page) => ((await coarse(page)) ? 44 : 30);

/** A comfortable table row: 40 px with a mouse, 48 px on touch. */
export const rowPx = async (page: Page) => ((await coarse(page)) ? 48 : 40);
