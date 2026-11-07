import '@fontsource-variable/manrope';
import '@fontsource/noto-sans-arabic/400.css';
import '@fontsource/noto-sans-arabic/700.css';
import '../index.css';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { I18nProvider } from '@medvya/i18n';
import { BrowserRouter } from 'react-router';
import { useLocaleController } from '../features/locale/hooks/useLocaleController';
import App from './App';

// Reads are few and every refused one is worth seeing: no focus refetch, no retry.
const queryClient = new QueryClient({ defaultOptions: { queries: { refetchOnWindowFocus: false, retry: false } } });

export default function Root() {
  const { locale, setLocale } = useLocaleController();
  return (
    <QueryClientProvider client={queryClient}>
      {/* The console serves both countries; IN only sets number and date formatting. */}
      <I18nProvider locale={locale} country="IN">
        <BrowserRouter>
          <App locale={locale} onLocaleChange={setLocale} />
        </BrowserRouter>
      </I18nProvider>
    </QueryClientProvider>
  );
}
