import type { Page } from '@playwright/test';

/**
 * Mocks the sign-in start (MDV-P1-059): GET /bff/login for the no-hint path, POST /bff/login with the work email in its body, answered
 * with a stand-in provider address. `leaks(word)` lists the requested URLs that contain the word (also URL-encoded).
 */
export async function mockSignInStart(page: Page) {
  const providerUrl = '/provider/authorize';
  const bodies: unknown[] = [];
  const urls: string[] = [];
  page.on('request', r => urls.push(r.url()));
  await page.route('**/bff/login**', r => {
    if (r.request().method() !== 'POST') return r.fulfill({ body: 'signing in' });
    bodies.push(r.request().postDataJSON());
    return r.fulfill({ json: { location: providerUrl } });
  });
  await page.route(`**${providerUrl}`, r => r.fulfill({ body: 'provider' }));
  return { providerUrl, bodies, leaks: (word: string) => urls.filter(u => decodeURIComponent(u).includes(word)) };
}
