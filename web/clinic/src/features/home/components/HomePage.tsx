import type { ReactNode } from 'react';
import { Box, Paper, Stack, Typography } from '@mui/material';
import { useI18n } from '@medvya/i18n';

export default function HomePage({ languageSwitcher }: { languageSwitcher: ReactNode }) {
  const { t } = useI18n();
  return (
    <Box component="main" sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', p: 2 }}>
      <Paper sx={{ p: 4, maxWidth: 480 }}>
        <Stack spacing={2}>
          <Typography variant="h1" component="h1">{t('app.name')}</Typography>
          <Typography variant="body1" color="text.secondary">{t('app.tagline')}</Typography>
          {languageSwitcher}
        </Stack>
      </Paper>
    </Box>
  );
}
