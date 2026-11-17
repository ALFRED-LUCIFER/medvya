import { defineConfig, devices } from '@playwright/test';

// shortcut: runs against the clinic Vite dev server; switch baseURL to staging once it exists (MDV-P0-022).
export default defineConfig({
  testDir: '.',
  testIgnore: 'fullstack/**', // needs Docker and .NET: `pnpm e2e:fullstack`
  forbidOnly: !!process.env.CI,
  // A private repo's runner has 2 vCPUs, where Playwright's default (half the cores) gives 1 worker; the specs mostly wait on the page, so 2 fit.
  workers: process.env.CI ? 2 : undefined,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
  // Reduced motion: styles.css then drops every transition, so axe never samples a button's colour mid-fade (a flaky false contrast failure).
  use: { baseURL: 'http://localhost:4173', trace: 'retain-on-failure', reducedMotion: 'reduce' },
  projects: [
    { name: 'chromium', testIgnore: ['fullstack/**', /-performance\.spec\.ts/], use: { ...devices['Desktop Chrome'] } },
    // MDV-P2-062: targets are 36 px with a mouse and 44 px on touch. Every "control-size targets" test runs again with touch, so each area
    // proves both sizes (support/targets.ts picks the size from any-pointer: coarse).
    { name: 'touch', grep: /control-size/, testIgnore: ['fullstack/**', /-performance\.spec\.ts/], use: { ...devices['Desktop Chrome'], hasTouch: true } },
    // Time budgets run alone, after every other spec has finished: measured beside 250 parallel specs they test the runner, not the page.
    // One at a time too: two budgets measured side by side share the CPU and the throttled link (MDV audit UX-12 added the second one).
    { name: 'performance', testMatch: /-performance\.spec\.ts/, dependencies: ['chromium'], workers: 1, use: { ...devices['Desktop Chrome'] } },
  ],
  webServer: [
    {
      command: 'pnpm --filter clinic exec vite --port 4173 --strictPort',
      url: 'http://localhost:4173',
      reuseExistingServer: !process.env.CI,
    },
    {
      // The production build (`pnpm -r build` first), for the first-render budget (schedule-performance.spec.ts).
      command: 'pnpm --filter clinic exec vite preview --port 4174 --strictPort',
      url: 'http://localhost:4174/app/', // the build's base path
      reuseExistingServer: !process.env.CI,
    },
    {
      // The platform console (MDV-P5-040): platform-console.spec.ts sets this baseURL.
      command: 'pnpm --filter platform-admin exec vite --port 4175 --strictPort',
      url: 'http://localhost:4175',
      reuseExistingServer: !process.env.CI,
    },
  ],
});
