import { createContext, useContext, useMemo, type ReactNode } from 'react';
import en from '../messages/en.json';
import hi from '../messages/hi.json';
import ar from '../messages/ar.json';
// shortcut: all three catalogs ship in the clinic's first-paint chunk (about 64 kB; with the registration copy it passed Vite's
// 500 kB chunk warning); load hi/ar on demand when the catalogs grow further or before the patient portal (P4).

export type Locale = 'en' | 'hi' | 'ar';
export type Country = 'IN' | 'AE';
export type MessageKey = keyof typeof en;

// shortcut: plain {var} interpolation, no plural/select; move to FormatJS (ICU) when the first plural string lands.
export const catalogs: Record<Locale, Partial<Record<MessageKey, string>>> = { en, hi, ar };
export const locales: { code: Locale; label: string }[] = [
  { code: 'en', label: 'English' }, { code: 'hi', label: 'हिन्दी' }, { code: 'ar', label: 'العربية' },
];

export function translate(locale: Locale, key: MessageKey, vars: Record<string, string | number> = {}) {
  const text = catalogs[locale][key] ?? en[key]; // missing translations fall back to English
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
  const value = useMemo(() => makeI18n(locale, country), [locale, country]);
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export const useI18n = () => useContext(I18nContext);
