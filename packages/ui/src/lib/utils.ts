import { clsx, type ClassValue } from 'clsx';
import { extendTailwindMerge } from 'tailwind-merge';

// The type tokens (MDV-P1-079) are font sizes; unknown text-* names would be read as colours, and a colour after them would drop them.
const twMerge = extendTailwindMerge({
  extend: { classGroups: { 'font-size': [{ text: ['display', 'title', 'heading', 'subheading', 'body', 'body-large', 'label', 'caption', 'mono'] }] } },
});

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

/** The element that scrolls `el`: its nearest scrolling ancestor, else the page. Pinned chrome reserves its space there. */
export const scrollContainer = (el: HTMLElement): HTMLElement => {
  for (let p = el.parentElement; p; p = p.parentElement) if (/(auto|scroll)/.test(getComputedStyle(p).overflowY)) return p;
  return document.documentElement;
};

/** In a fixed layer (a sheet, dialog or popover): scrolling the page never moves it, so a focus scroll must leave the page alone (MDV-P1-094 AC3). */
export const inFixedLayer = (el: HTMLElement): boolean => {
  for (let p: HTMLElement | null = el; p; p = p.parentElement) if (getComputedStyle(p).position === "fixed") return true;
  return false;
};
