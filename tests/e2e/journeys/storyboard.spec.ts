import { expect, test } from '@playwright/test';
import { storyboard } from './storyboard';

// Guards the parser: a silently empty journey would hide every step.
test('every journey J1–J6 has its storyboard steps', () => {
  const counts = ['J1', 'J2', 'J3', 'J4', 'J5', 'J6'].map(id => storyboard(id).steps.length);
  expect(counts).toEqual([8, 8, 7, 6, 7, 6]);
  for (const s of storyboard('J1').steps) expect(s.screen).toMatch(/^[A-Z]-\d\d$/);
});
