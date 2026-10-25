import { defineConfig, devices } from '@playwright/test';

// shortcut: runs against the clinic Vite dev server; switch baseURL to staging once it exists (MDV-P0-022).
export default defineConfig({
  testDir: '.',
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? 'github' : 'list',
  use: { baseURL: 'http://localhost:4173', trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'pnpm --filter clinic exec vite --port 4173 --strictPort',
      url: 'http://localhost:4173',
      reuseExistingServer: !process.env.CI,
    },
    {
      // The production build (`pnpm -r build` first), for the first-render budget (schedule-performance.spec.ts).
      command: 'pnpm --filter clinic exec vite preview --port 4174 --strictPort',
      url: 'http://localhost:4174',
      reuseExistingServer: !process.env.CI,
    },
  ],
});
