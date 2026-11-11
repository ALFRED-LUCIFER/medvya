// Starts the real stack for the full-stack browser journeys (MDV-P0-029) and keeps it up until it is signalled:
// a throwaway PostgreSQL 16 in Docker, the synthetic cast seeded by the real generator, the production web build,
// and the real Host.Api (Development, dev sign-in provider) serving that build at /app/ on E2E_API_PORT.
// Everything is removed on exit. Synthetic data only; the SQL password is random per run and never leaves this machine.
import { execFileSync, spawn, spawnSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { cpSync, mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import net from 'node:net';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const apiPort = process.env.E2E_API_PORT ?? '5391';
const clockStart = process.env.E2E_CLOCK_START ?? '2026-11-03T04:00:00Z'; // Tuesday 09:30 in Asia/Kolkata
const password = `Pw_${randomBytes(12).toString('hex')}_1A`;
const container = `medvya-e2e-sql-${process.pid}`;
const blobContainer = `medvya-e2e-blob-${process.pid}`;
const mailContainer = `medvya-e2e-mail-${process.pid}`;
const work = mkdtempSync(join(tmpdir(), 'medvya-e2e-'));
const log = m => console.log(`[stack] ${m}`);
let api;

const freePort = () => new Promise((ok, fail) => {
  const s = net.createServer().listen(0, '127.0.0.1', () => { const { port } = s.address(); s.close(() => ok(port)); });
  s.on('error', fail);
});

function cleanup() {
  if (api?.pid) try { process.kill(-api.pid, 'SIGTERM'); } catch { /* already gone */ }
  spawnSync('docker', ['rm', '-f', container, blobContainer, mailContainer], { stdio: 'ignore' });
  rmSync(work, { recursive: true, force: true });
}
for (const sig of ['SIGTERM', 'SIGINT', 'SIGHUP']) process.on(sig, () => { cleanup(); process.exit(0); });
process.on('exit', cleanup);

const run = (cmd, args, env = {}) => execFileSync(cmd, args, { cwd: repo, stdio: 'inherit', env: { ...process.env, ...env } });

try {
  const sqlPort = await freePort();
  log(`PostgreSQL on 127.0.0.1:${sqlPort}`);
  execFileSync('docker', ['run', '-d', '--rm', '--name', container, '-p', `127.0.0.1:${sqlPort}:5432`,
    '-e', 'POSTGRES_DB=medvya', '-e', 'POSTGRES_USER=medvya', '-e', `POSTGRES_PASSWORD=${password}`, 'postgres:16'], { stdio: 'ignore' });
  // Private Blob for the onboarding documents (MDV-P1-062): a throwaway Azurite whose account key is random per run.
  const blobPort = await freePort();
  const blobKey = randomBytes(48).toString('base64');
  execFileSync('docker', ['run', '-d', '--rm', '--name', blobContainer, '-p', `127.0.0.1:${blobPort}:10000`, '-e', `AZURITE_ACCOUNTS=medvyae2e:${blobKey}`,
    'mcr.microsoft.com/azure-storage/azurite:latest', 'azurite-blob', '--blobHost', '0.0.0.0', '--loose', '--skipApiVersionCheck'], { stdio: 'ignore' });
  // Mail sink for the emails a journey sends (pharmacy secure link, MDV-P2-020): without it the send fails and nothing is shared.
  const mailPort = await freePort();
  execFileSync('docker', ['run', '-d', '--rm', '--name', mailContainer, '-p', `127.0.0.1:${mailPort}:25`, 'rnwood/smtp4dev:latest'], { stdio: 'ignore' });
  const as = user => `Host=127.0.0.1;Port=${sqlPort};Database=medvya;Username=${user};Password=${password}`;
  const connection = as('medvya_app'); // the API and the seeder run as the application role, so row-level security applies

  log('building the web app and the API while PostgreSQL starts');
  run('pnpm', ['--filter', 'clinic', 'build']);
  mkdirSync(join(work, 'wwwroot'));
  cpSync(join(repo, 'web/clinic/dist'), join(work, 'wwwroot/app'), { recursive: true });
  for (const project of ['src/Host.Api', 'tools/synthetic-data-generator', 'tools/db-migrate']) run('dotnet', ['build', project, '--nologo', '-v', 'q']);

  for (let i = 0; ; i++) {
    const ready = spawnSync('docker', ['exec', container, 'pg_isready', '-h', '127.0.0.1', '-U', 'medvya', '-d', 'medvya'], { stdio: 'ignore' });
    if (ready.status === 0) break;
    if (i > 90) throw new Error('PostgreSQL did not become ready in 3 minutes.');
    await new Promise(r => setTimeout(r, 2000));
  }

  log('creating roles and migrating (admin, then the migrator login)');
  run('dotnet', ['run', '--no-build', '--project', 'tools/db-migrate', '--', 'admin', as('medvya'), `medvya_app=${password}`, `medvya_migrator=${password}`]);
  run('dotnet', ['run', '--no-build', '--project', 'tools/db-migrate', '--', 'migrate', as('medvya_migrator')]);

  log('seeding the synthetic cast (two tenants)');
  run('dotnet', ['run', '--no-build', '--project', 'tools/synthetic-data-generator', '--', '--uae'],
    { DOTNET_ENVIRONMENT: 'Development', ConnectionStrings__Medvya: connection });

  log(`starting Host.Api on http://localhost:${apiPort}`);
  api = spawn('dotnet', ['run', '--no-build', '--no-launch-profile', '--project', 'src/Host.Api'], {
    cwd: repo, detached: true, stdio: ['ignore', 'inherit', 'inherit'],
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: `http://localhost:${apiPort}`,
      ASPNETCORE_WEBROOT: join(work, 'wwwroot'),
      Authentication__Oidc__Authority: `http://localhost:${apiPort}/dev-idp`,
      ConnectionStrings__Medvya: connection,
      Email__Smtp__Host: '127.0.0.1',
      Email__Smtp__Port: String(mailPort),
      Storage__ConnectionString: `DefaultEndpointsProtocol=http;AccountName=medvyae2e;AccountKey=${blobKey};BlobEndpoint=http://127.0.0.1:${blobPort}/medvyae2e;`,
      // Medvya operations for the approval API (MDV-P1-064): the seeded practice admin K. Lakshmi stands in for the operator (a Development stack only).
      Operations__OrganisationApprovers__0: '5e1d0001-0002-4000-8000-000000000007',
      Web__AppPath: '/app/', // as on Azure: server-made redirects (sign-in refused, signed out) go to the app, not the public site
      Testing__ClockStartUtc: clockStart,
      // Every browser here is one client IP, and Host.Api also serves the app's scripts: the per-IP budget (300/min) is for a real desk.
      RateLimiting__PermitsPerMinute: '3000',
      RateLimiting__SignInPerMinute: '600', // the suite signs in over 20 times a minute from this one IP; the 20/min default is for a real desk
    },
  });
  api.on('exit', code => { log(`Host.Api exited (${code})`); process.exit(1); });
  await new Promise(() => {}); // Playwright polls /health, runs the tests, then signals this process
} catch (e) {
  console.error(e);
  process.exit(1);
}
