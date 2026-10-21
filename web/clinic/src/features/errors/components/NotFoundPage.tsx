import { Box } from '@mui/material';
import { SystemState } from '@medvya/ui';

export default function NotFoundPage({ onBackToToday }: { onBackToToday: () => void }) {
  return (
    <Box component="main" sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', p: 2 }}>
      <SystemState state="notFound" onAction={onBackToToday} />
    </Box>
  );
}
