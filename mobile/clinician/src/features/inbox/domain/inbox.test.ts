import { cast } from '../../../dev/cast';
import { inboxCount } from './inbox';

it('counts results only for clinicians, messages for everyone', () => {
  expect(inboxCount(cast.inbox, true)).toBe(3); // 2 unacknowledged results + 1 message
  expect(inboxCount(cast.inbox, false)).toBe(1); // front desk cannot see results
});
