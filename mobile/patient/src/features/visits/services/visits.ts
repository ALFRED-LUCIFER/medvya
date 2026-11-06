import { fromCast } from '../../api/services/source';

export const fetchVisits = (_personId: string) => fromCast(c => c.visits);
