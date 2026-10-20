import { Box, Paper, Typography } from '@mui/material';
import UiGallery from './UiGallery';

// shortcut: path check instead of a router; React Router arrives with the app shell (MDV-P0-022).
export default function App() {
  if (window.location.pathname === '/__ui') return <UiGallery />;
  return (
    <Box component="main" sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', p: 2 }}>
      <Paper sx={{ p: 4, maxWidth: 480 }}>
        <Typography variant="h1" component="h1">Medvya</Typography>
        <Typography variant="body1" color="text.secondary">Clinic app · foundation build</Typography>
      </Paper>
    </Box>
  );
}
