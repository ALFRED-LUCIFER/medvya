import { createTheme } from '@mui/material/styles';

// AI-provisional palette used only for unreviewed AI output (Foundations: ai.600).
declare module '@mui/material/styles' {
  interface Palette { ai: Palette['primary'] }
  interface PaletteOptions { ai?: PaletteOptions['primary'] }
}

// Verbatim from the Handoff "MUI theme (excerpt)", see docs/02-design/components-theme-copy.md.
export const medvyaTheme = createTheme({
  palette: {
    mode: 'light',
    primary:   { main: '#0E9384', dark: '#0F766E', contrastText: '#FFFFFF' },
    secondary: { main: '#2563EB' },
    error:     { main: '#B42333' },   // clinical critical only
    warning:   { main: '#975A16' },   // uncertainty / attention
    success:   { main: '#087F5B' },   // confirmed, never inferred
    ai:        { main: '#7155D8', light: '#F2EFFC' },
    background:{ default: '#F7FAFC', paper: '#FFFFFF' },
    text:      { primary: '#142536', secondary: '#506579' },
    divider: '#DFE7ED',
  },
  shape: { borderRadius: 10 },
  spacing: 8,
  typography: {
    fontFamily: 'Manrope, system-ui, sans-serif',
    h1: { fontSize: 24, fontWeight: 700, lineHeight: '32px' },
    h2: { fontSize: 18, fontWeight: 700, lineHeight: '26px' },
    body1: { fontSize: 15, fontWeight: 500, lineHeight: '24px' },
    body2: { fontSize: 14, lineHeight: '20px' },
    caption: { fontSize: 12, fontWeight: 600 },
  },
  components: {
    MuiButton: { defaultProps: { disableElevation: true }, styleOverrides: { root: { minHeight: 44, fontWeight: 700, textTransform: 'none' } } },
    MuiPaper:  { styleOverrides: { root: { border: '1px solid #DFE7ED', boxShadow: 'none' } } },
    MuiTableCell: { styleOverrides: { root: { fontVariantNumeric: 'tabular-nums' } } },
  },
  breakpoints: { values: { xs: 0, sm: 360, md: 768, lg: 1280, xl: 1600 } },
});
