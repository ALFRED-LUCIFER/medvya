import { useMemo } from 'react';
import createCache from '@emotion/cache';
import { CacheProvider } from '@emotion/react';
import { CssBaseline, ThemeProvider } from '@mui/material';
import { createTheme } from '@mui/material/styles';
import { prefixer } from 'stylis';
import rtlPlugin from 'stylis-plugin-rtl';
import '@fontsource-variable/manrope';
import '@fontsource/noto-sans-arabic/400.css';
import '@fontsource/noto-sans-arabic/700.css';
import { I18nProvider } from '@medvya/i18n';
import { medvyaTheme } from '@medvya/ui';
import { useLocaleController } from '../features/locale/hooks/useLocaleController';
import App from './App';

const caches = {
  ltr: createCache({ key: 'mui' }),
  rtl: createCache({ key: 'muirtl', stylisPlugins: [prefixer, rtlPlugin] }),
};

export default function Root() {
  const { locale, dir, setLocale } = useLocaleController();
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
