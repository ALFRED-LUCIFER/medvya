import { useI18n } from '@medvya/i18n';
import { Button, ErrorSummary, Field, Input } from '@medvya/ui';
import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import type { ClosureForm, ClosureWithVisits, LocationChoice } from '../domain/settings';
import { fieldError } from './fieldStyles';
import { LocationSelect } from './LocationSelect';
import { Section } from './Section';

type Holidays = {
  form: UseFormReturn<ClosureForm>;
  closures: ClosureWithVisits[];
  loading: boolean;
  loadFailed: boolean;
  forbidden: boolean;
  adding: boolean;
  addError: 'duplicate' | 'failed' | null;
  removingId: string | null;
  removeFailed: boolean;
  onSubmit: FormEventHandler<HTMLFormElement>;
  onRemove: (id: string) => void;
};

type Props = { choice: LocationChoice; holidays: Holidays; zoneOf: (locationId: string) => string };

/** Holidays (F-08): closed dates. A closed date takes no new bookings; visits already booked are listed so the desk can move them. */
export default function HolidaysPanel({ choice, holidays: h, zoneOf }: Props) {
  const { t, formatShortDay, formatTime } = useI18n();
  const { errors, submitCount } = h.form.formState;
  const required = t('settings.error.required');
  const fields = [
    { id: 'holiday-date', label: t('settings.holidays.date'), message: errors.date && required },
    { id: 'holiday-name', label: t('settings.holidays.name'), message: errors.name && required },
  ];
  if (choice.locationsLoading) return <p role="status">{t('settings.loading')}</p>;
  if (choice.locationsFailed) return <p role="alert" className={fieldError}>{t('settings.loadFailed')}</p>;
  return (
    <div className="flex flex-col gap-4">
      <LocationSelect id="holidays-location" locations={choice.locations} value={choice.locationId} onChange={choice.onLocation} />
      <Section title={t('settings.holidays.title')} intro={t('settings.holidays.intro')}>
        <form noValidate onSubmit={h.onSubmit} className="flex flex-wrap items-start gap-3">
          <ErrorSummary title={t('field.errorSummary')} focusKey={submitCount} className="basis-full"
            errors={fields.flatMap(f => (f.message ? [{ id: f.id, label: f.label, message: f.message }] : []))} />
          <Field id="holiday-date" label={t('settings.holidays.date')} required error={fields[0]!.message}>
            <Input type="date" dir="ltr" className="w-48" {...h.form.register('date')} />
          </Field>
          <Field id="holiday-name" label={t('settings.holidays.name')} required error={fields[1]!.message} className="min-w-60 flex-1">
            <Input autoComplete="off" placeholder={t('settings.holidays.placeholder')} {...h.form.register('name')} />
          </Field>
          <div className="pt-[1.625rem]"><Button type="submit" loading={h.adding}>{t('settings.holidays.add')}</Button></div>
        </form>
        <div aria-live="polite">
          {h.addError && <p role="alert" className={fieldError}>{t(h.addError === 'duplicate' ? 'settings.holidays.duplicate' : 'settings.saveFailed')}</p>}
          {h.removeFailed && <p role="alert" className={fieldError}>{t('settings.saveFailed')}</p>}
        </div>
        {h.loading ? <p role="status">{t('settings.loading')}</p>
          : h.loadFailed ? <p role="alert" className={fieldError}>{t(h.forbidden ? 'settings.forbidden' : 'settings.loadFailed')}</p>
          : h.closures.length === 0 ? <p className="text-muted-foreground">{t('settings.holidays.empty')}</p> : (
          <ul className="flex flex-col divide-y rounded-lg border">
            {h.closures.map(({ closure, appointmentsToMove }) => (
              <li key={closure.id} className="flex flex-col gap-2 p-3">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <span className="flex flex-col">
                    <span className="font-bold">{closure.name}</span>
                    <span className="text-sm text-muted-foreground">{formatShortDay(closure.date)}</span>
                  </span>
                  <Button type="button" variant="outline" loading={h.removingId === closure.id} onClick={() => h.onRemove(closure.id)}
                    aria-label={t('settings.holidays.removeNamed', { name: closure.name, date: formatShortDay(closure.date) })}>{t('settings.remove')}</Button>
                </div>
                {appointmentsToMove.length > 0 && (
                  <div className="rounded-lg border border-warning-700 p-3">
                    <p className="font-bold">{t('settings.holidays.toMove', { count: appointmentsToMove.length })}</p>
                    <ul className="mt-1 flex flex-col gap-1 text-sm">
                      {appointmentsToMove.map(a => (
                        <li key={a.id}>{t('settings.holidays.visit', { reference: a.reference, time: formatTime(new Date(a.start), zoneOf(choice.locationId)), calendar: a.calendarName })}</li>
                      ))}
                    </ul>
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </Section>
    </div>
  );
}
