import { Box, Paper, Stack, Typography } from '@mui/material';
import { useI18n, type Locale } from '@medvya/i18n';
import LanguageSwitcher from './LanguageSwitcher';
import UiGallery from './UiGallery';

type Props = { locale: Locale; onLocaleChange: (l: Locale) => void };

// shortcut: path check instead of a router; React Router arrives with the app shell (MDV-P0-022).
export default function App({ locale, onLocaleChange }: Props) {
  const { t } = useI18n();
  const switcher = <LanguageSwitcher value={locale} onChange={onLocaleChange} />;
  if (window.location.pathname === '/__ui') return <UiGallery switcher={switcher} />;
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
