import { defineConfig, devices } from '@playwright/test';

// MDV-P0-029: the browser against the real Host.Api and a real SQL Server (no mocked APIs). `pnpm e2e:fullstack` needs Docker and the .NET SDK;
// stack.mjs starts and removes everything. Not part of `pnpm e2e` (that one is mocked and needs neither).
// Limit: the browser's clock is fixed 30 minutes after the API's start time, so a stack that idles for more than ~30 minutes before the
// tests run (or runs them for that long) offers slots the API considers past. A normal run is under 2 minutes. The stack is always fresh.
const port = process.env.E2E_API_PORT ?? '5391';
process.env.E2E_API_PORT = port;
process.env.E2E_CLOCK_START ??= '2026-11-03T04:00:00Z'; // the API's clock and the browser's start here: Tuesday 09:30 in Kolkata

export default defineConfig({
  testDir: '.',
  workers: 1, // one shared database, and the journey is a story: its steps run in order
  retries: 0,
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? 'github' : 'list',
  timeout: 90_000,
  use: { baseURL: `http://localhost:${port}`, trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'node stack.mjs',
    url: `http://localhost:${port}/health`,
    reuseExistingServer: !!process.env.E2E_REUSE_STACK, // while writing specs: start `node fullstack/stack.mjs` once; its data is not reset
    timeout: 600_000, // image start under emulation, two builds and the seed
    gracefulShutdown: { signal: 'SIGTERM', timeout: 30_000 },
    stdout: 'pipe',
    stderr: 'pipe',
  },
});
