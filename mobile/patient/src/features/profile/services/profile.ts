import { fromCast } from '../../api/services/source';

export const fetchProfile = (_personId: string) => fromCast(c => c.profile);
export const fetchFamily = (_personId: string) => fromCast(c => c.people);
