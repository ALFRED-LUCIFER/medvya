import { I18nProvider, localeFor } from '@medvya/i18n';
import { BrandProvider, clinicBrand } from '@medvya/ui-native';
import { onlineManager, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { getLocales } from 'expo-localization';
import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { useState } from 'react';
import { watchNetwork } from '../features/api/services/network';
import { SessionProvider, useSession } from '../features/session/hooks/session';
import { protectScreen } from '../features/session/services/session';

// shortcut: locale and country follow the device; use the signed-in user's language and the practice's country once sign-in lands (MDV-P5-034).
const device = getLocales()[0];
// Before the first screen renders: no screenshots or switcher preview of patient data (ADR-011 §6), and offline is known.
void protectScreen();
onlineManager.setEventListener(watchNetwork);

/** Which screens exist depends on the session: signed out, locked, or in. Nothing behind the lock renders while locked. */
function Routes() {
  const { ready, session, locked } = useSession();
  if (!ready) return null; // the stored session is read first, so a signed-in phone opens on the lock screen, not sign-in
  return (
    <Stack screenOptions={{ headerShown: false }}>
      <Stack.Protected guard={!session}>
        <Stack.Screen name="sign-in" />
      </Stack.Protected>
      <Stack.Protected guard={!!session && locked}>
        <Stack.Screen name="lock" options={{ gestureEnabled: false, animation: 'fade' }} />
      </Stack.Protected>
      <Stack.Protected guard={!!session && !locked}>
        <Stack.Screen name="(tabs)" />
        <Stack.Screen name="patients/[id]" />
        <Stack.Screen name="account" options={{ presentation: 'modal' }} />
      </Stack.Protected>
    </Stack>
  );
}

export default function RootLayout() {
  const [queryClient] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: 1 } } }));
  return (
    <I18nProvider locale={localeFor(device?.languageCode)} country={device?.regionCode === 'AE' ? 'AE' : 'IN'}>
      <BrandProvider value={clinicBrand}>
        <QueryClientProvider client={queryClient}>
          <SessionProvider>
            <StatusBar style="dark" />
            <Routes />
          </SessionProvider>
        </QueryClientProvider>
      </BrandProvider>
    </I18nProvider>
  );
}
