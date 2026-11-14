import { fromCast } from '../../api/services/source';
import { readCopy, saveCopy, storeGeneration } from '../../api/services/store';
import type { Today, TodayCopy } from '../domain/today';

/** Online: today's list, kept as the encrypted last copy. Offline: that copy, if it is less than 24 hours old (MDV-P5-034). */
export async function fetchToday(facilityId: string, online: boolean): Promise<TodayCopy> {
  const key = `today:${facilityId}`;
  if (!online) {
    const copy = await readCopy<Today>(key);
    if (!copy) throw new Error('mobile.error.offline');
    return { today: copy.body, savedAt: copy.savedAt };
  }
  const since = storeGeneration();
  const today = await fromCast(c => c.today);
  await saveCopy(key, today, since);
  return { today, savedAt: null };
}
