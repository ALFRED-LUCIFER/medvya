import '@fontsource-variable/manrope';
import '@fontsource/noto-sans-arabic/400.css';
import '@fontsource/noto-sans-arabic/700.css';
import '../index.css';
import { I18nProvider } from '@medvya/i18n';
import { useLocaleController } from '../features/locale/hooks/useLocaleController';
import App from './App';

// Right-to-left comes from <html dir> (set by the locale controller) plus Tailwind logical utilities (ms-*, ps-*, start-*).
export default function Root() {
  const { locale, setLocale } = useLocaleController();
  // shortcut: country fixed to IN until the tenant context exists (MDV-P0-012 /v1/me/context).
  return (
    <I18nProvider locale={locale} country="IN">
      <App locale={locale} onLocaleChange={setLocale} />
    </I18nProvider>
  );
}
