import { post, send } from '../../api/services/api';
import { apiBase, fromCast } from '../../api/services/source';
import { readCopy, saveCopy, storeGeneration } from '../../api/services/store';
import type { Session } from '../../session/domain/session';
import { todayFromBoard, type AllergyState, type Board, type Today, type TodayCopy } from '../domain/today';

/**
 * Today from the server: the board (GET /v1/front-desk/board, as the web's Today), and for a doctor the notes to sign and the
 * next patient's allergy state. Patient ids go in the body, never the URL. A failed count or flag is left blank, never guessed.
 */
async function fromServer({ staff, facility }: Session, reasonText: (category: string) => string): Promise<Today> {
  const doctor = staff.role === 'doctor';
  const [board, pending] = await Promise.all([
    send<Board>(`/v1/front-desk/board?facilityId=${encodeURIComponent(facility.id)}`),
    doctor ? send<{ items: unknown[] }>('/v1/notes/pending').catch(() => null) : null,
  ]);
  const today = todayFromBoard(board, doctor ? staff.id : null, reasonText);
  today.toReview = pending ? pending.items.length : null;
  const next = today.next;
  if (doctor && next) {
    const flags = await post<{ items: { patientId: string; state: AllergyState }[] }>('/v1/chart/allergy-flags', { patientIds: [next.patientId] }).catch(() => null);
    const state = flags?.items.find(i => i.patientId === next.patientId)?.state;
    if (state) next.flags = [{ kind: 'allergies', state }];
  }
  return today;
}

/** Online: today's list, kept as the encrypted last copy. Offline: that copy, if it is less than 24 hours old (MDV-P5-034). */
export async function fetchToday(session: Session, online: boolean, reasonText: (category: string) => string): Promise<TodayCopy> {
  const key = `today:${session.facility.id}`;
  if (!online) {
    const copy = await readCopy<Today>(key);
    if (!copy) throw new Error('mobile.error.offline');
    return { today: copy.body, savedAt: copy.savedAt };
  }
  const since = storeGeneration();
  const today = apiBase() ? await fromServer(session, reasonText) : await fromCast(c => c.today);
  await saveCopy(key, today, since);
  return { today, savedAt: null };
}
