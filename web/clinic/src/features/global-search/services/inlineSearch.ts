import { inlineInputId, inlinePanelId } from '../domain/lookup';

// The inline patient search in the top bar (768 px and wider). On a phone it is hidden and the search dialog opens instead.

/** Focuses the inline search box when it is on screen; false on a phone, where the box is hidden. */
export function focusInlineSearch(): boolean {
  const input = document.getElementById(inlineInputId);
  if (!input || input.offsetParent === null) return false;
  input.focus();
  return true;
}

/** Tab from the box moves into the open results (their actions), which sit in a layer outside the page's tab order; false when none is open. */
export function focusInlinePanel(): boolean {
  const first = document.getElementById(inlinePanelId)?.querySelector<HTMLElement>('a[href], button:not([disabled]), [role=combobox]:not(input)');
  if (!first) return false;
  first.focus();
  return true;
}
