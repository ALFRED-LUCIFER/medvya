import { fromCast } from '../../api/services/source';

export const fetchMessages = (_personId: string) => fromCast(c => c.messages);
