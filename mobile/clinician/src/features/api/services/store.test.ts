import { files, keychain } from '../../../test/fakes';
import { KEEP_MS, readCopy, saveCopy, storeGeneration, wipeStore } from './store';

afterEach(() => wipeStore());

it('serves a copy for 24 hours, then deletes it (ADR-011 §3)', async () => {
  await saveCopy('today:f', { n: 1 }, storeGeneration(), 0);
  expect(await readCopy('today:f', KEEP_MS - 1)).toEqual({ body: { n: 1 }, savedAt: 0 });
  expect(await readCopy('today:f', KEEP_MS)).toBeNull();
  expect(await readCopy('today:f', 0)).toBeNull(); // deleted, not just hidden
});

it('drops a copy fetched before a wipe, so a sign-out racing a fetch keeps nothing', async () => {
  const since = storeGeneration();
  await wipeStore();
  await saveCopy('today:f', { n: 1 }, since);
  expect(files.size).toBe(0);
  expect(await readCopy('today:f')).toBeNull();
});

it('wipes the database file and its key', async () => {
  await saveCopy('today:f', { n: 1 }, storeGeneration());
  expect([files.size, keychain.size]).toEqual([1, 1]);
  await wipeStore();
  expect([files.size, keychain.size]).toEqual([0, 0]);
});
