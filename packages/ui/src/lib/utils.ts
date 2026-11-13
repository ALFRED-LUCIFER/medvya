import { clsx, type ClassValue } from 'clsx';
import { extendTailwindMerge } from 'tailwind-merge';

// The type tokens (MDV-P1-079) are font sizes; unknown text-* names would be read as colours, and a colour after them would drop them.
const twMerge = extendTailwindMerge({
  extend: { classGroups: { 'font-size': [{ text: ['display', 'title', 'heading', 'subheading', 'body', 'body-large', 'label', 'caption', 'mono'] }] } },
});

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
