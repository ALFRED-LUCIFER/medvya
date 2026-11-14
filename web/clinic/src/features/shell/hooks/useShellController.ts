import { useMemo } from 'react';
import { useSessionController } from '../../session/hooks/useSessionController';
import { mobileTabs, visibleNav } from '../domain/navigation';

/** Session plus the navigation this membership may see (capabilities and tenant flags from /v1/me/context). */
export function useShellController() {
  const session = useSessionController();
  const active = session.session?.active ?? null;
  const nav = useMemo(() => {
    const items = visibleNav(active);
    return { items, tabs: mobileTabs(items) };
  }, [active]);
  return { ...session, ...nav };
}
