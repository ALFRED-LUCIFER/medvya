import { useMemo, useState } from 'react';
import { useSessionController } from '../../session/hooks/useSessionController';
import { mobileTabs, visibleNav } from '../domain/navigation';
import { readNavCollapsed, saveNavCollapsed } from '../services/navPreference';

/** Session plus the navigation this membership may see (capabilities and tenant flags from /v1/me/context). */
export function useShellController() {
  const session = useSessionController();
  const active = session.session?.active ?? null;
  const nav = useMemo(() => {
    const items = visibleNav(active);
    return { items, tabs: mobileTabs(items) };
  }, [active]);
  // The 1280 px sidebar folds to the labelled rail only by the user's own choice, kept per user (MDV-P1-093).
  const userId = session.session?.user.id ?? '';
  const [folded, setFolded] = useState<Record<string, boolean>>({});
  const navCollapsed = userId !== '' && (folded[userId] ?? readNavCollapsed(userId));
  const toggleNav = () => {
    saveNavCollapsed(userId, !navCollapsed);
    setFolded(f => ({ ...f, [userId]: !navCollapsed }));
  };
  return { ...session, ...nav, navCollapsed, toggleNav };
}
