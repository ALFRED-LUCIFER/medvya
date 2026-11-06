import { fromCast, NotConnectedError } from './source';

it('refuses synthetic data outside a Development build', async () => {
  await expect(fromCast(c => c.staff, false)).rejects.toBeInstanceOf(NotConnectedError);
});

it('serves the synthetic cast in a Development build', async () => {
  expect((await fromCast(c => c.staff, true)).length).toBeGreaterThan(0);
});
