import { I18nProvider } from '@medvya/i18n';
import { BrandProvider, clinicBrand } from '@medvya/ui-native';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react-native';
import type { ReactNode } from 'react';
import { SessionProvider } from '../features/session/hooks/session';

/** Renders with the same providers as app/_layout.tsx, English, fresh cache. */
export function renderApp(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { gcTime: Infinity } } });
  return render(
    <I18nProvider locale="en" country="IN">
      <BrandProvider value={clinicBrand}>
        <QueryClientProvider client={client}><SessionProvider>{ui}</SessionProvider></QueryClientProvider>
      </BrandProvider>
    </I18nProvider>,
  );
}
