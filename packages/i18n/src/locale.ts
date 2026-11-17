import { useEffect, useState } from 'react';
import type { Locale } from './index';

const KEY = 'medvya.locale';

export const isLocale = (v: unknown): v is Locale => v === 'en' || v === 'hi' || v === 'ar';
export const directionOf = (l: Locale): 'ltr' | 'rtl' => (l === 'ar' ? 'rtl' : 'ltr');

// The UI language is a per-device convenience, never patient data. Storage may be blocked (private mode).
export function readLocale(): Locale {
  try { const v = localStorage.getItem(KEY); return isLocale(v) ? v : 'en'; } catch { return 'en'; }
}

function saveLocale(l: Locale): void {
  try { localStorage.setItem(KEY, l); } catch { /* keep in memory only */ }
}

/** The web apps' chosen UI language: remembered on the device and applied to <html lang dir>. */
export function useLocaleController() {
  const [locale, setLocale] = useState<Locale>(readLocale);
  const dir = directionOf(locale);
  useEffect(() => {
    document.documentElement.lang = locale;
    document.documentElement.dir = dir;
    saveLocale(locale);
  }, [locale, dir]);
  return { locale, dir, setLocale };
}
