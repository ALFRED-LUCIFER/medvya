import { fromCast, NotConnectedError } from './source';

it('refuses synthetic data outside a Development build', async () => {
  await expect(fromCast(c => c.staff, false)).rejects.toBeInstanceOf(NotConnectedError);
});

it('serves the synthetic cast in a Development build', async () => {
  expect((await fromCast(c => c.staff, true)).length).toBeGreaterThan(0);
});

it('never mixes the synthetic cast with a server: a build with one refuses it', async () => {
  process.env.EXPO_PUBLIC_MEDVYA_API = 'https://api.test';
  try { await expect(fromCast(c => c.staff, true)).rejects.toBeInstanceOf(NotConnectedError); }
  finally { delete process.env.EXPO_PUBLIC_MEDVYA_API; }
});
