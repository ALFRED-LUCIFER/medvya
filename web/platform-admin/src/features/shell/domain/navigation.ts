import type { PlatformRole } from '../../session/domain/session';

export type NavId = 'overview' | 'approvals' | 'team' | 'audit';
export type NavItem = { id: NavId; path: string; roles: readonly PlatformRole[] };

const everyone: readonly PlatformRole[] = ['Owner', 'Operations', 'Support', 'Finance', 'Reviewer'];

/**
 * The console's pages, each with the roles the server lets in (the server decides; this only hides links that would answer 403).
 * shortcut: the design's 16 pages arrive with their tickets (MDV-P5-041 to P5-049, P5-017); each adds its item here.
 */
export const navItems: readonly NavItem[] = [
  { id: 'overview', path: '/', roles: everyone },
  { id: 'approvals', path: '/approvals', roles: everyone },
  { id: 'team', path: '/team', roles: ['Owner'] },
  { id: 'audit', path: '/audit', roles: ['Owner', 'Operations'] },
];

export const navFor = (role: PlatformRole) => navItems.filter(i => i.roles.includes(role));
