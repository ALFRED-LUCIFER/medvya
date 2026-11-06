import { fromCast } from '../../api/services/source';

export const fetchRecords = (_personId: string) => fromCast(c => c.records);
