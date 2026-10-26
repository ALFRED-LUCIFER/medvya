import '@fontsource-variable/manrope';
import '@fontsource/noto-sans-arabic/400.css';
import '@fontsource/noto-sans-arabic/700.css';
import '../index.css';
import { QueryClientProvider } from '@tanstack/react-query';
import { I18nProvider } from '@medvya/i18n';
import { lazy, Suspense } from 'react';
import { BrowserRouter, Route, Routes } from 'react-router';
import { useLocaleController } from '../features/locale/hooks/useLocaleController';
import App from './App';
import { createQueryClient } from './queryClient';

const queryClient = createQueryClient();
// The waiting-room TV loads on its own: it never needs the staff app, and a TV never loads the staff app.
const QueueTvRoute = lazy(() => import('./QueueTvRoute'));

// Right-to-left comes from <html dir> (set by the locale controller) plus Tailwind logical utilities (ms-*, ps-*, start-*).
export default function Root() {
  const { locale, setLocale } = useLocaleController();
  // Signed out there is no tenant yet: IN formats until App switches to the active tenant's country (/v1/me/context).
  return (
    <QueryClientProvider client={queryClient}>
      <I18nProvider locale={locale} country="IN">
        <BrowserRouter>
          <Routes>
            {/* Display-token TV (MDV-P1-031): outside the staff app, so it never asks for a staff session or context. */}
            <Route path="/queue/tv" element={<Suspense><QueueTvRoute locale={locale} onLocaleChange={setLocale} /></Suspense>} />
            <Route path="*" element={<App locale={locale} onLocaleChange={setLocale} />} />
          </Routes>
        </BrowserRouter>
      </I18nProvider>
    </QueryClientProvider>
  );
}
