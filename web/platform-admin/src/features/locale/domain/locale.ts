import type { Locale } from '@medvya/i18n';

export const isLocale = (v: unknown): v is Locale => v === 'en' || v === 'hi' || v === 'ar';
export const directionOf = (l: Locale): 'ltr' | 'rtl' => (l === 'ar' ? 'rtl' : 'ltr');
