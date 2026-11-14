import { isFrontDeskView, type FrontDeskView } from '../domain/board';

// The front desk view this user chose last (MDV-P2-052): a view name per user, never patient data. Storage may be blocked (private mode).
const key = (userId: string) => `medvya.frontDesk.view.${userId}`;

/** The last choice, or the board (the default, owner decision Q14) when there is none or storage is blocked. */
export function readFrontDeskView(userId: string): FrontDeskView {
  try {
    const stored = localStorage.getItem(key(userId));
    return isFrontDeskView(stored) ? stored : 'board';
  } catch { return 'board'; }
}

export function saveFrontDeskView(userId: string, view: FrontDeskView): void {
  try { localStorage.setItem(key(userId), view); } catch { /* keep in memory only */ }
}
