import '@fontsource-variable/manrope';
import '@fontsource/noto-sans-arabic/400.css';
import '@fontsource/noto-sans-arabic/700.css';
import '../index.css';
import { QueryClientProvider } from '@tanstack/react-query';
import { I18nProvider, useLocaleController } from '@medvya/i18n';
import { BrowserRouter } from 'react-router';
import LoadErrorBoundary from '../features/errors/components/LoadErrorBoundary';
import LoadFailedPage from '../features/errors/components/LoadFailedPage';
import App from './App';
import { createQueryClient } from './queryClient';

const queryClient = createQueryClient();

export default function Root() {
  const { locale, setLocale } = useLocaleController();
  return (
    <QueryClientProvider client={queryClient}>
      {/* The console serves both countries; IN only sets number and date formatting. */}
      <I18nProvider locale={locale} country="IN">
        <LoadErrorBoundary fallback={<LoadFailedPage onReload={() => window.location.reload()} />}>
          <BrowserRouter>
            <App locale={locale} onLocaleChange={setLocale} />
          </BrowserRouter>
        </LoadErrorBoundary>
      </I18nProvider>
    </QueryClientProvider>
  );
}
