import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, ErrorSummary, Field, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Textarea } from '@medvya/ui';
import { timeZones } from '../domain/settings';
import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import type { AddLocationForm, CalendarTemplate, LocationChoice, ResourceForm } from '../domain/settings';
import { fieldError } from './fieldStyles';
import { LocationSelect } from './LocationSelect';
import { Section } from './Section';

type Props = LocationChoice & {
  form: UseFormReturn<AddLocationForm>;
  editingId: string | null;
  onEdit: (id: string | null) => void;
  saving: boolean;
  saveProblem: MessageKey | null;
  certificateError: MessageKey | null;
  /** Changes once per failed save: the error summary takes focus then. */
  errorFocusKey: string;
  certificateName: string | null;
  onCertificate: (file: File | undefined) => void;
  added: string | null;
  onSubmit: FormEventHandler<HTMLFormElement>;
  resources: CalendarTemplate[];
  resourcesFailed: boolean;
  resourceForm: UseFormReturn<ResourceForm>;
  addingResource: boolean;
  addResourceFailed: boolean;
  onAddResource: FormEventHandler<HTMLFormElement>;
};

/** Locations (F-08): the practice's sites, and the rooms or resources (such as a procedure room) bookable at each. */
export default function LocationsPanel(p: Props) {
  const { t } = useI18n();
  const { errors } = p.form.formState;
  const roomError = p.resourceForm.formState.errors.name && t('settings.error.required');
  const fields = [
    { id: 'location-name', label: t('settings.locations.name'), message: errors.name && t('settings.error.required') },
    { id: 'location-address', label: t('settings.locations.address'), message: errors.address && t('settings.error.tooLong') },
    { id: 'location-tax', label: t('settings.locations.taxNumber'), message: errors.taxNumber && t(errors.taxNumber.message as MessageKey) },
    { id: 'location-certificate', label: t('settings.locations.certificate'), message: p.certificateError ? t(p.certificateError) : undefined },
  ];
  const error = (id: string) => fields.find(f => f.id === id)?.message || undefined;
  const zone = p.form.watch('timeZone');
  const editing = p.locations.find(l => l.id === p.editingId);
  const zones = zone && !(timeZones as readonly string[]).includes(zone) ? [...timeZones, zone] : timeZones;
  if (p.locationsLoading) return <p role="status">{t('settings.loading')}</p>;
  if (p.locationsFailed) return <p role="alert" className={fieldError}>{t('settings.loadFailed')}</p>;
  return (
    <div className="flex flex-col gap-4">
      <Section title={t('settings.locations.title')} intro={t('settings.locations.intro')}>
        <ul className="flex flex-col divide-y rounded-lg border">
          {p.locations.map(l => (
            <li key={l.id} className="flex flex-wrap items-center justify-between gap-3 p-3">
              <span className="flex min-w-0 flex-col">
                <span className="font-bold">{l.name}</span>
                <span className="text-sm text-muted-foreground">{[l.address, l.timeZone].filter(Boolean).join(' · ')}</span>
              </span>
              <Button type="button" variant="outline" className="min-w-11" aria-label={t('settings.locations.editNamed', { name: l.name })} onClick={() => p.onEdit(l.id)}>{t('settings.edit')}</Button>
            </li>
          ))}
        </ul>
        <form noValidate onSubmit={p.onSubmit} className="grid gap-4 sm:grid-cols-2" aria-label={editing ? t('settings.locations.editNamed', { name: editing.name }) : t('settings.locations.add')}>
          <h3 className="font-bold sm:col-span-2">{editing ? t('settings.locations.editNamed', { name: editing.name }) : t('settings.locations.add')}</h3>
          <ErrorSummary title={t('field.errorSummary')} focusKey={p.errorFocusKey} className="sm:col-span-2"
            errors={fields.flatMap(f => (f.message ? [{ id: f.id, label: f.label, message: f.message }] : []))} />
          <Field id="location-name" label={t('settings.locations.name')} required error={error('location-name')}>
            <Input autoComplete="off" {...p.form.register('name')} />
          </Field>
          <Select value={zone} onValueChange={v => p.form.setValue('timeZone', v, { shouldDirty: true })}>
            <Field id="location-zone" label={t('settings.locations.timeZone')} required hint={editing ? undefined : t('settings.locations.timeZoneHint')}>
              <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
            </Field>
            <SelectContent>{zones.map(z => <SelectItem key={z} value={z}>{z}</SelectItem>)}</SelectContent>
          </Select>
          <Field id="location-address" label={t('settings.locations.address')} error={error('location-address')} className="sm:col-span-2">
            <Textarea rows={2} {...p.form.register('address')} />
          </Field>
          {!editing && (
            <>
              <Field id="location-tax" label={t('settings.locations.taxNumber')} required hint={t('settings.locations.taxNumberHint')} error={error('location-tax')}>
                <Input autoComplete="off" {...p.form.register('taxNumber')} />
              </Field>
              <Field id="location-certificate" label={t('settings.locations.certificate')} required hint={t('settings.locations.certificateHint')} error={error('location-certificate')}>
                <Input type="file" accept="application/pdf,image/png,image/jpeg" className="h-auto py-2" onChange={e => p.onCertificate(e.currentTarget.files?.[0])} />
              </Field>
              {p.certificateName && <p role="status" className="sm:col-span-2">{t('settings.locations.certificateChosen', { name: p.certificateName })}</p>}
            </>
          )}
          <div className="flex flex-wrap items-center gap-3 sm:col-span-2" aria-live="polite">
            <Button type="submit" loading={p.saving}>{editing ? t('settings.save') : t('settings.locations.addButton')}</Button>
            {editing && <Button type="button" variant="outline" onClick={() => p.onEdit(null)}>{t('settings.cancel')}</Button>}
            {p.saveProblem && <span role="alert" className={fieldError}>{t(p.saveProblem)}</span>}
            {p.added && <span role="status">{t('settings.locations.added', { name: p.added })}</span>}
          </div>
        </form>
      </Section>
      <Section title={t('settings.rooms.title')} intro={t('settings.rooms.intro')}>
        <LocationSelect id="rooms-location" locations={p.locations} value={p.locationId} onChange={p.onLocation} />
        {p.resourcesFailed && <p role="alert" className={fieldError}>{t('settings.loadFailed')}</p>}
        {p.resources.length === 0 ? <p className="text-muted-foreground">{t('settings.rooms.empty')}</p> : (
          <ul className="flex flex-col divide-y rounded-lg border">
            {p.resources.map(r => <li key={r.calendarId} className="p-3 font-bold">{r.name}</li>)}
          </ul>
        )}
        <form noValidate onSubmit={p.onAddResource} className="flex flex-wrap items-end gap-3">
          <ErrorSummary title={t('field.errorSummary')} focusKey={p.resourceForm.formState.submitCount} className="basis-full"
            errors={roomError ? [{ id: 'room-name', label: t('settings.rooms.name'), message: roomError }] : []} />
          <Field id="room-name" label={t('settings.rooms.name')} required error={roomError || undefined} className="min-w-60 flex-1">
            <Input autoComplete="off" placeholder={t('settings.rooms.placeholder')} {...p.resourceForm.register('name')} />
          </Field>
          <Button type="submit" loading={p.addingResource}>{t('settings.rooms.add')}</Button>
          {p.addResourceFailed && <span role="alert" className={fieldError}>{t('settings.saveFailed')}</span>}
        </form>
      </Section>
    </div>
  );
}
