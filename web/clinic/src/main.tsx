import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { CssBaseline, ThemeProvider } from '@mui/material';
import { medvyaTheme } from '@medvya/ui';
import App from './App';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider theme={medvyaTheme}>
      <CssBaseline />
      <App />
    </ThemeProvider>
  </StrictMode>,
);
