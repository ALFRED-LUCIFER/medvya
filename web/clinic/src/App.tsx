import { Box, Paper, Stack, Typography } from '@mui/material';
import { useI18n, type Locale } from '@medvya/i18n';
import { SystemState } from '@medvya/ui';
import LanguageSwitcher from './LanguageSwitcher';
import UiGallery from './UiGallery';
import useOnline from './useOnline';

type Props = { locale: Locale; onLocaleChange: (l: Locale) => void };

// shortcut: path check instead of a router; React Router arrives with the app shell (MDV-P0-022).
function Page({ locale, onLocaleChange }: Props) {
  const { t } = useI18n();
  const switcher = <LanguageSwitcher value={locale} onChange={onLocaleChange} />;
  const path = window.location.pathname;
  if (path === '/__ui') return <UiGallery switcher={switcher} />;
  if (path !== '/') return (
    <Box component="main" sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', p: 2 }}>
      <SystemState state="notFound" onAction={() => window.location.assign('/')} />
    </Box>
  );
  return (
    <Box component="main" sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', p: 2 }}>
      <Paper sx={{ p: 4, maxWidth: 480 }}>
        <Stack spacing={2}>
          <Typography variant="h1" component="h1">{t('app.name')}</Typography>
          <Typography variant="body1" color="text.secondary">{t('app.tagline')}</Typography>
          {switcher}
        </Stack>
      </Paper>
    </Box>
  );
}

export default function App(props: Props) {
  const { online, retry } = useOnline();
  return (
    <>
      {/* Offline is a banner over the page, not a replacement, so typed input survives (F-05d). */}
      {!online && (
        <Box sx={{ position: 'fixed', insetInlineEnd: 16, bottom: 16, zIndex: 1300 }}>
          <SystemState state="offline" onAction={retry} />
        </Box>
      )}
      <Page {...props} />
    </>
  );
}
