import { defineConfig, devices } from '@playwright/test';

// shortcut: runs against the clinic Vite dev server; switch baseURL to staging once it exists (MDV-P0-022).
export default defineConfig({
  testDir: '.',
  testIgnore: 'fullstack/**', // needs Docker and .NET: `pnpm e2e:fullstack`
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
  // Reduced motion: styles.css then drops every transition, so axe never samples a button's colour mid-fade (a flaky false contrast failure).
  use: { baseURL: 'http://localhost:4173', trace: 'retain-on-failure', reducedMotion: 'reduce' },
  projects: [
    { name: 'chromium', testIgnore: ['fullstack/**', /-performance\.spec\.ts/], use: { ...devices['Desktop Chrome'] } },
    // Time budgets run alone, after every other spec has finished: measured beside 250 parallel specs they test the runner, not the page.
    { name: 'performance', testMatch: /-performance\.spec\.ts/, dependencies: ['chromium'], use: { ...devices['Desktop Chrome'] } },
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
