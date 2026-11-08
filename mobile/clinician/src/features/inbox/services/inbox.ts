import { fromCast } from '../../api/services/source';

export const fetchInbox = () => fromCast(c => c.inbox);
