import { fromCast } from '../../api/services/source';

export const fetchToday = (_facilityId: string) => fromCast(c => c.today);
