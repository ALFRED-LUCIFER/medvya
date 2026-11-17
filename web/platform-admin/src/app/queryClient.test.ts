import { describe, expect, it } from 'vitest';
import { ApiError } from '../features/api/services/api';
import { createQueryClient } from './queryClient';

describe('console query client', () => {
  it.each([[401, true], [403, false], [500, false]])('a %i re-reads the session: %s', async (code, reread) => {
    const client = createQueryClient();
    client.setQueryData(['me'], { displayName: 'Ops' });
    await client.fetchQuery({ queryKey: ['plans'], queryFn: () => Promise.reject(new ApiError(code)) }).catch(() => undefined);
    expect(client.getQueryState(['me'])!.isInvalidated).toBe(reread);
  });
});
