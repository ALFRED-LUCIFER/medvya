import type { Locale } from '@medvya/i18n';
import { isLocale } from '../domain/locale';

const KEY = 'medvya.locale';

// The UI language is a per-device convenience, never patient data. Storage may be blocked (private mode).
export function readLocale(): Locale {
  try { const v = localStorage.getItem(KEY); return isLocale(v) ? v : 'en'; } catch { return 'en'; }
}

export function saveLocale(l: Locale): void {
  try { localStorage.setItem(KEY, l); } catch { /* keep in memory only */ }
}

export function applyToDocument(l: Locale, dir: 'ltr' | 'rtl'): void {
  document.documentElement.lang = l;
  document.documentElement.dir = dir;
}
