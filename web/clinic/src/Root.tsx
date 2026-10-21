import { useEffect, useMemo, useState } from 'react';
import createCache from '@emotion/cache';
import { CacheProvider } from '@emotion/react';
import { CssBaseline, ThemeProvider } from '@mui/material';
import { createTheme } from '@mui/material/styles';
import { prefixer } from 'stylis';
import rtlPlugin from 'stylis-plugin-rtl';
import '@fontsource-variable/manrope';
import '@fontsource/noto-sans-arabic/400.css';
import '@fontsource/noto-sans-arabic/700.css';
import { I18nProvider, type Locale } from '@medvya/i18n';
import { medvyaTheme } from '@medvya/ui';
import App from './App';

const caches = {
  ltr: createCache({ key: 'mui' }),
  rtl: createCache({ key: 'muirtl', stylisPlugins: [prefixer, rtlPlugin] }),
};

// The UI language is a per-device convenience, never patient data.
const readLocale = (): Locale => {
  try { const l = localStorage.getItem('medvya.locale'); return l === 'hi' || l === 'ar' ? l : 'en'; } catch { return 'en'; }
};

export default function Root() {
  const [locale, setLocale] = useState<Locale>(readLocale);
  const dir = locale === 'ar' ? 'rtl' : 'ltr';
  useEffect(() => {
    document.documentElement.lang = locale;
    document.documentElement.dir = dir;
    try { localStorage.setItem('medvya.locale', locale); } catch { /* storage blocked: keep in memory */ }
  }, [locale, dir]);
  const theme = useMemo(() => createTheme(medvyaTheme, { direction: dir }), [dir]);
  // shortcut: country fixed to IN until the tenant context exists (MDV-P0-012 /v1/me/context).
  return (
    <CacheProvider value={caches[dir]}>
      <ThemeProvider theme={theme}>
        <I18nProvider locale={locale} country="IN">
          <CssBaseline />
          <App locale={locale} onLocaleChange={setLocale} />
        </I18nProvider>
      </ThemeProvider>
    </CacheProvider>
  );
}
