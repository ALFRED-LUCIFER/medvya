import { I18nProvider, localeFor } from '@medvya/i18n';
import { getLocales } from 'expo-localization';
import { Stack } from 'expo-router';

// shortcut: locale and country follow the device; use the signed-in user's language and the practice's country once sign-in lands (MDV-P5-034).
const device = getLocales()[0];

export default function RootLayout() {
  return (
    <I18nProvider locale={localeFor(device?.languageCode)} country={device?.regionCode === 'AE' ? 'AE' : 'IN'}>
      <Stack screenOptions={{ headerShown: false }} />
    </I18nProvider>
  );
}
