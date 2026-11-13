import type { ReactNode } from 'react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { CircleAlert } from 'lucide-react';
import { useI18n, type Country } from '@medvya/i18n';
import { ErrorSummary, Field, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { errorCode, stepFields, type FormField, type WizardForm } from '../domain/onboarding';

export type FormProps = { form: UseFormReturn<WizardForm> };

/** The error of a group of radios or checkboxes (a fieldset, not a Field), styled as Field's. */
export function FieldError({ id, code }: { id: string; code: string | undefined }) {
  const { t } = useI18n();
  if (!code) return null;
  return (
    <p id={id} className="flex items-start gap-1.5 text-label text-danger-text">
      <CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />{t(`onboarding.error.${errorCode(code)}`)}
    </p>
  );
}

const useError = (form: UseFormReturn<WizardForm>, name: FormField) => {
  const { t } = useI18n();
  const code = form.formState.errors[name]?.message;
  return code ? t(`onboarding.error.${errorCode(code)}`) : undefined;
};

export function TextField({ form, name, label, required, hint, ltr, wide, type = 'text', inputMode }: FormProps & {
  name: FormField; label: string; required?: boolean; hint?: ReactNode; ltr?: boolean; wide?: boolean;
  type?: 'text' | 'email' | 'time' | 'date'; inputMode?: 'numeric' | 'email';
}) {
  return (
    <Field id={name} label={label} required={required} error={useError(form, name)} hint={hint} className={wide ? 'md:col-span-2' : undefined}>
      <Input {...form.register(name)} type={type} inputMode={inputMode} dir={ltr ? 'ltr' : undefined} autoComplete="off" spellCheck={false} />
    </Field>
  );
}

/** A choice from a fixed list (shadcn Select). Value "" = nothing chosen. */
export function SelectField({ form, name, label, required, hint, options, optionLabel, placeholder }: FormProps & {
  name: FormField; label: string; required?: boolean; hint?: ReactNode; options: readonly string[]; optionLabel: (value: string) => string; placeholder: string;
}) {
  const error = useError(form, name);
  return (
    <Controller control={form.control} name={name} render={({ field }) => (
      <Select value={field.value as string} onValueChange={field.onChange}>
        <Field id={name} label={label} required={required} error={error} hint={hint}>
          <SelectTrigger className="w-full" onBlur={field.onBlur}><SelectValue placeholder={placeholder} /></SelectTrigger>
        </Field>
        <SelectContent>{options.map(o => <SelectItem key={o} value={o}>{optionLabel(o)}</SelectItem>)}</SelectContent>
      </Select>
    )} />
  );
}

/**
 * After a failed Continue: this step's fields in error, each linked by the control's id (a group links to its first choice).
 * It takes focus when focusKey changes.
 */
export function StepErrorSummary({ form, step, country, currency, focusKey }: FormProps & { step: number; country: Country; currency: string; focusKey: number }) {
  const { t } = useI18n();
  const labels: Partial<Record<FormField, string>> = {
    orgType: t('onboarding.orgType.legend'), mode: t('onboarding.mode.legend'), fullName: t('onboarding.field.fullName'), clinicName: t('onboarding.field.clinicName'),
    address: t('onboarding.field.address'), contactEmail: t('onboarding.field.contactEmail'), specialties: t('onboarding.specialties.legend'),
    authority: t(`onboarding.field.authority.${country}`), number: t(`onboarding.field.licenceNumber.${country}`), expiresOn: t('onboarding.field.licenceExpiry'),
    attested: t('onboarding.attest.legend'), qualifications: t('onboarding.field.qualifications'), languages: t('onboarding.field.languages'),
    registrationNumber: t(`onboarding.field.registration.${country}`), taxId: t(`onboarding.field.taxId.${country}`),
    days: t('onboarding.days.legend'), weekdayStart1: t('onboarding.hours.morning'), saturdayStart: t('onboarding.hours.saturdayOwn'),
    slotMinutes: t('onboarding.field.slot'), newVisitFee: t('onboarding.field.newFee', { currency }), followUpFee: t('onboarding.field.followUpFee', { currency }),
    inviteEmail: t('onboarding.field.inviteEmail'), patients: t('onboarding.patients.legend'),
  };
  const errors = (stepFields[step] ?? []).flatMap(name => {
    const code = form.formState.errors[name]?.message, label = labels[name];
    return code && label ? [{ id: name, label, message: t(`onboarding.error.${errorCode(code)}`) }] : [];
  });
  return <ErrorSummary title={t('field.errorSummary')} focusKey={focusKey} errors={errors} />;
}
