import type { ReactNode } from 'react';
import { useI18n } from '@medvya/i18n';
import { Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import type { Location } from '../domain/settings';
import { fieldError } from './fieldStyles';

/** A label above its control, with the error (if any) announced and tied to the control through `id-error`. */
export function Field({ id, label, error, hint, className = '', children }: { id: string; label: string; error?: string | null; hint?: string; className?: string; children: ReactNode }) {
  return (
    <div className={`flex min-w-0 flex-col gap-2 ${className}`}>
      <Label htmlFor={id}>{label}</Label>
      {children}
      {hint && <p id={`${id}-hint`} className="text-sm text-muted-foreground">{hint}</p>}
      {error && <p id={`${id}-error`} role="alert" className={fieldError}>{error}</p>}
    </div>
  );
}

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

/** A tab panel's card: heading, one sentence of what it is for, then content. */
export function Section({ title, intro, children }: { title: string; intro?: string; children: ReactNode }) {
  return (
    <section aria-label={title} className="flex flex-col gap-4 rounded-xl border bg-card p-6">
      <h2 className="text-lg font-extrabold">{title}</h2>
      {intro && <p className="text-muted-foreground">{intro}</p>}
      {children}
    </section>
  );
}
