import { MenuItem, TextField } from '@mui/material';
import { locales, useI18n, type Locale } from '@medvya/i18n';

export default function LanguageSwitcher({ value, onChange }: { value: Locale; onChange: (l: Locale) => void }) {
  const { t } = useI18n();
  return (
    <TextField select size="small" label={t('app.language')} value={value} onChange={e => onChange(e.target.value as Locale)} sx={{ minWidth: 160 }}>
      {locales.map(l => <MenuItem key={l.code} value={l.code} lang={l.code}>{l.label}</MenuItem>)}
    </TextField>
  );
}
