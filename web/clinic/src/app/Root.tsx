import '@fontsource-variable/manrope';
import '@fontsource/noto-sans-arabic/400.css';
import '@fontsource/noto-sans-arabic/700.css';
import '../index.css';
import { QueryClientProvider } from '@tanstack/react-query';
import { I18nProvider } from '@medvya/i18n';
import { BrowserRouter } from 'react-router';
import { useLocaleController } from '../features/locale/hooks/useLocaleController';
import App from './App';
import { createQueryClient } from './queryClient';

const queryClient = createQueryClient();

// Right-to-left comes from <html dir> (set by the locale controller) plus Tailwind logical utilities (ms-*, ps-*, start-*).
export default function Root() {
  const { locale, setLocale } = useLocaleController();
  // Signed out there is no tenant yet: IN formats until App switches to the active tenant's country (/v1/me/context).
  return (
    <QueryClientProvider client={queryClient}>
      <I18nProvider locale={locale} country="IN">
        <BrowserRouter>
          <App locale={locale} onLocaleChange={setLocale} />
        </BrowserRouter>
      </I18nProvider>
    </QueryClientProvider>
  );
}
