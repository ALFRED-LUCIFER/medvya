import { createTheme } from '@mui/material/styles';

// AI-provisional palette used only for unreviewed AI output (Foundations: ai.600).
declare module '@mui/material/styles' {
  interface Palette { ai: Palette['primary'] }
  interface PaletteOptions { ai?: PaletteOptions['primary'] }
}
declare module '@mui/material/Button' {
  interface ButtonPropsColorOverrides { ai: true }
}

// Handoff: visible 2px focus ring on all interactive elements.
const focusRing = { outline: '2px solid #2563EB', outlineOffset: 2 };

// From the Handoff "MUI theme (excerpt)"; deviations are marked inline and listed in docs/02-design/README.md.
export const medvyaTheme = createTheme({
  palette: {
    mode: 'light',
    // Deviation: Handoff main #0E9384 gives white 14px text only 3.8:1 (fails WCAG AA); brand.700 passes at 5.5:1.
    primary:   { main: '#0F766E', dark: '#0B5C55', contrastText: '#FFFFFF' },
    secondary: { main: '#2563EB' },
    error:     { main: '#B42333' },   // clinical critical only
    warning:   { main: '#975A16' },   // uncertainty / attention
    success:   { main: '#087F5B' },   // confirmed, never inferred
    ai:        { main: '#7155D8', light: '#F2EFFC', dark: '#5B3FC4', contrastText: '#FFFFFF' }, // dark/contrastText added so <Button color="ai"> works
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
    MuiButtonBase: { styleOverrides: { root: { '&.Mui-focusVisible': focusRing } } },
    MuiOutlinedInput: { styleOverrides: { root: { '&.Mui-focused': focusRing } } },
    MuiButton: { defaultProps: { disableElevation: true }, styleOverrides: { root: { minHeight: 44, fontWeight: 700, textTransform: 'none' } } },
    MuiPaper:  { styleOverrides: { root: { border: '1px solid #DFE7ED', boxShadow: 'none' } } },
    MuiTableCell: { styleOverrides: { root: { fontVariantNumeric: 'tabular-nums' } } },
  },
  breakpoints: { values: { xs: 0, sm: 360, md: 768, lg: 1280, xl: 1600 } },
});
