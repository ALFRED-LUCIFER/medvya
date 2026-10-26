import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import en from '../messages/en.json';

export type Locale = 'en' | 'hi' | 'ar';
export type Country = 'IN' | 'AE';
export type MessageKey = keyof typeof en;

// shortcut: plain {var} interpolation, no plural/select; move to FormatJS (ICU) when the first plural string lands.
// English ships with the app (it is the fallback); Hindi and Arabic load on demand, so first paint carries one catalog, not three.
export const catalogs: { en: Partial<Record<MessageKey, string>> } & Partial<Record<Locale, Partial<Record<MessageKey, string>>>> = { en };
const loaders: Record<Exclude<Locale, 'en'>, () => Promise<{ default: Partial<Record<MessageKey, string>> }>> = {
  hi: () => import('../messages/hi.json'),
  ar: () => import('../messages/ar.json'),
};

/** Loads a locale's catalog once; if it cannot load (offline), that locale falls back to English rather than staying blank. */
export async function loadCatalog(locale: Locale): Promise<void> {
  if (catalogs[locale]) return;
  try { catalogs[locale] = (await loaders[locale as Exclude<Locale, 'en'>]()).default; }
  catch { catalogs[locale] = {}; }
}
export const locales: { code: Locale; label: string }[] = [
  { code: 'en', label: 'English' }, { code: 'hi', label: 'हिन्दी' }, { code: 'ar', label: 'العربية' },
];

export function translate(locale: Locale, key: MessageKey, vars: Record<string, string | number> = {}) {
  const text = catalogs[locale]?.[key] ?? en[key]; // missing translations (or a catalog not loaded) fall back to English
  return text.replace(/\{(\w+)\}/g, (m, name: string) => (name in vars ? String(vars[name]) : m));
}

export function makeI18n(locale: Locale, country: Country) {
  // Western digits in every locale, matching the designs (Arabic screens show 10:00, 18 Oct).
  const tag = `${locale}-${country}-u-nu-latn`;
  return {
    locale, country,
    dir: (locale === 'ar' ? 'rtl' : 'ltr') as 'rtl' | 'ltr',
    t: (key: MessageKey, vars?: Record<string, string | number>) => translate(locale, key, vars),
    formatNumber: (n: number) => new Intl.NumberFormat(tag).format(n),
    formatDate: (d: Date) => new Intl.DateTimeFormat(tag, { day: '2-digit', month: 'short', year: 'numeric' }).format(d),
    /** 24-hour time in an IANA zone: schedules show facility time whatever the device's zone is. */
    formatTime: (d: Date, timeZone: string) => new Intl.DateTimeFormat(tag, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone }).format(d),
    /** A calendar date (YYYY-MM-DD) as a long day, e.g. "Monday, 19 October 2026". */
    formatDay: (date: string) => new Intl.DateTimeFormat(tag, { dateStyle: 'full', timeZone: 'UTC' }).format(new Date(`${date}T12:00:00Z`)),
    /** A calendar date (YYYY-MM-DD) as a short day for narrow headers, e.g. "Mon, 19 Oct". */
    formatShortDay: (date: string) => new Intl.DateTimeFormat(tag, { weekday: 'short', day: 'numeric', month: 'short', timeZone: 'UTC' }).format(new Date(`${date}T12:00:00Z`)),
    /** Money is stored in integer minor units (PRD v2 P4 NFR). */
    formatMoney: (minor: number, currency: 'INR' | 'AED') => new Intl.NumberFormat(tag, { style: 'currency', currency }).format(minor / 100),
  };
}

export type I18n = ReturnType<typeof makeI18n>;
const I18nContext = createContext<I18n>(makeI18n('en', 'IN'));

export function I18nProvider({ locale, country, children }: { locale: Locale; country: Country; children: ReactNode }) {
  const [loaded, setLoaded] = useState(() => Boolean(catalogs[locale]));
  useEffect(() => {
    let current = true;
    if (catalogs[locale]) setLoaded(true);
    else { setLoaded(false); void loadCatalog(locale).then(() => { if (current) setLoaded(true); }); }
    return () => { current = false; };
  }, [locale]);
  const value = useMemo(() => makeI18n(locale, country), [locale, country]);
  // Waits for the chosen catalog, so Hindi and Arabic never flash English (or the wrong direction) first.
  if (!loaded && !catalogs[locale]) return null;
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export const useI18n = () => useContext(I18nContext);
