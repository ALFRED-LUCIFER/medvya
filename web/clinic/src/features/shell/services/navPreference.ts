// Whether this user folded the 1280 px sidebar to the rail: a per-user layout choice, never patient data. Storage may be blocked (private mode).
const key = (userId: string) => `medvya.nav.collapsed.${userId}`;

export function readNavCollapsed(userId: string): boolean {
  try { return localStorage.getItem(key(userId)) === '1'; } catch { return false; }
}

export function saveNavCollapsed(userId: string, collapsed: boolean): void {
  try { if (collapsed) localStorage.setItem(key(userId), '1'); else localStorage.removeItem(key(userId)); } catch { /* keep in memory only */ }
}
