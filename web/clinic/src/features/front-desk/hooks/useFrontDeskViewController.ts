import { useState } from 'react';
import type { FrontDeskView } from '../domain/board';
import { readFrontDeskView, saveFrontDeskView } from '../services/viewPreference';

/** The desk's view switch (MDV-P2-052): starts at this user's last choice (the board until she chooses) and remembers each new one. */
export function useFrontDeskViewController(userId: string) {
  const [view, setView] = useState(() => readFrontDeskView(userId));
  return {
    view,
    onView: (next: FrontDeskView) => {
      saveFrontDeskView(userId, next);
      setView(next);
    },
  };
}
