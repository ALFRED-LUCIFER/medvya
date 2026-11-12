import type { Page } from '@playwright/test';

/** Orders and Referrals sit in a closed disclosure on the consultation until used (MDV audit P1-04): open it by its title. */
export const openPart = (page: Page, title: string) => page.locator('summary').filter({ hasText: new RegExp(`^${title}$`) }).click();
