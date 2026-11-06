import { useI18n } from '@medvya/i18n';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import type { Location } from '../domain/settings';
import { Field } from './Field';

/** The location a tab is looking at. Shown only when the practice has more than one. */
export function LocationSelect({ id, locations, value, onChange }: { id: string; locations: Location[]; value: string; onChange: (id: string) => void }) {
  const { t } = useI18n();
  if (locations.length < 2) return null;
  return (
    <Field id={id} label={t('settings.location')} className="max-w-sm">
      <Select value={value} onValueChange={onChange}>
        <SelectTrigger id={id} className="h-11 w-full"><SelectValue /></SelectTrigger>
        <SelectContent>{locations.map(l => <SelectItem key={l.id} value={l.id}>{l.name}</SelectItem>)}</SelectContent>
      </Select>
    </Field>
  );
}
