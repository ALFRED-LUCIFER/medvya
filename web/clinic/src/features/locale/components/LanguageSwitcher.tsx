import { useId } from 'react';
import { locales, useI18n, type Locale } from '@medvya/i18n';
import { Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';

export default function LanguageSwitcher({ value, onChange }: { value: Locale; onChange: (l: Locale) => void }) {
  const { t } = useI18n();
  const id = useId();
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>{t('app.language')}</Label>
      <Select value={value} onValueChange={v => onChange(v as Locale)}>
        <SelectTrigger id={id} className="min-h-11 min-w-40"><SelectValue /></SelectTrigger>
        <SelectContent>
          {locales.map(l => <SelectItem key={l.code} value={l.code} lang={l.code}>{l.label}</SelectItem>)}
        </SelectContent>
      </Select>
    </div>
  );
}
