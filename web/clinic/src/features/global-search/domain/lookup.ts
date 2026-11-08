// Global search (⌘K, MDV-P2-003): POST /v1/patients/lookup. A result is initials, age and the MRN tail only; the server says whether the
// caller may open the chart. Nothing here grants anything.
export type LookupAccess = 'Open' | 'CanRequest' | 'RequestPending' | 'NoAccess';
export type LookupRow = { id: string; initials: string; ageYears: number; ageEstimated: boolean; mrnTail: string; access: LookupAccess };
export type LookupResult = { items: LookupRow[]; total: number };

/** Each search is audited and rate limited, so a search starts only for text that can match something. */
export const minQueryLength = 2;
export const searchableText = (text: string) => (text.trim().length >= minQueryLength ? text.trim() : null);

/** Cmd+K on macOS, Ctrl+K elsewhere. */
export const isShortcut = (e: Pick<KeyboardEvent, 'key' | 'metaKey' | 'ctrlKey' | 'altKey' | 'shiftKey'>) =>
  e.key.toLowerCase() === 'k' && (e.metaKey || e.ctrlKey) && !e.altKey && !e.shiftKey;

/** The highlighted row after an arrow key: wraps at both ends; none when there are no rows. */
export function moveActive(index: number, key: 'ArrowDown' | 'ArrowUp', count: number): number {
  if (count === 0) return -1;
  return key === 'ArrowDown' ? (index + 1) % count : (index - 1 + count) % count;
}
