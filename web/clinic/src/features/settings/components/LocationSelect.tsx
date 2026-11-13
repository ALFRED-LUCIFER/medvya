import { useI18n } from '@medvya/i18n';
import { Field, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import type { Location } from '../domain/settings';

/** The location a tab is looking at. Shown only when the practice has more than one. */
export function LocationSelect({ id, locations, value, onChange }: { id: string; locations: Location[]; value: string; onChange: (id: string) => void }) {
  const { t } = useI18n();
  if (locations.length < 2) return null;
  return (
    <Select value={value} onValueChange={onChange}>
      <Field id={id} label={t('settings.location')} className="max-w-sm">
        <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
      </Field>
      <SelectContent>{locations.map(l => <SelectItem key={l.id} value={l.id}>{l.name}</SelectItem>)}</SelectContent>
    </Select>
  );
}
