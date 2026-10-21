import { useMemo } from 'react';
import { useSessionController } from '../../session/hooks/useSessionController';
import { mobileTabs, navGroups, visibleNav } from '../domain/navigation';

/** Session plus the navigation this membership may see (capabilities and tenant flags from /v1/me/context). */
export function useShellController() {
  const session = useSessionController();
  const active = session.session?.active ?? null;
  const nav = useMemo(() => {
    const items = visibleNav(active);
    return {
      items,
      groups: navGroups.map(group => ({ group, items: items.filter(i => i.group === group) })).filter(g => g.items.length > 0),
      tabs: mobileTabs(items),
    };
  }, [active]);
  return { ...session, ...nav };
}
