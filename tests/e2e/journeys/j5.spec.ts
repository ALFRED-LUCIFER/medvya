import { expect } from '@playwright/test';
import { mockOrders } from '../support/orders';
import { journey } from './storyboard';

// Storyboard: docs/02-design/journeys.md. Implement steps as their screens ship.
// shortcut: step 1 runs on the mocked API only. On the real stack the seeded doctor's licence is unverified (Medvya operations verify it,
// MDV-P1-010), which keeps "Prescriptions and orders" off, so the real-stack step needs that verification in the seed first.
journey('J5', {
  // MDV-P2-018: the doctor adds a test in the consultation, places the order on purpose and it reads Ordered (icon and label).
  1: async ({ page }) => {
    await mockOrders(page);
    await page.goto('/consult/e-1');
    await page.getByLabel('Test', { exact: true }).fill('HbA1c');
    await page.getByRole('button', { name: 'Add test' }).click();
    await expect(page.getByRole('row', { name: /HbA1c/ })).toContainText('Draft');
    await page.getByRole('button', { name: 'Place orders' }).click();
    await expect(page.getByRole('row', { name: /HbA1c/ })).toContainText('Ordered');
  },
});
