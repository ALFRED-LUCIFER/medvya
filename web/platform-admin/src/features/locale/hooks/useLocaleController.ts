import { useEffect, useState } from 'react';
import type { Locale } from '@medvya/i18n';
import { directionOf } from '../domain/locale';
import { applyToDocument, readLocale, saveLocale } from '../services/localePreference';

export function useLocaleController() {
  const [locale, setLocale] = useState<Locale>(readLocale);
  const dir = directionOf(locale);
  useEffect(() => { applyToDocument(locale, dir); saveLocale(locale); }, [locale, dir]);
  return { locale, dir, setLocale };
}
