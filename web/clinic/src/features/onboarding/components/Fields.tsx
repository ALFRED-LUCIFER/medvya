import type { ReactNode } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { CircleAlert } from 'lucide-react';
import { useI18n, type Country } from '@medvya/i18n';
import { errorCode, stepFields, type FormField, type WizardForm } from '../domain/onboarding';
import { TextField as FormTextField } from '../../forms/components/TextField';
import { SelectField as FormSelectField } from '../../forms/components/SelectField';
import { StepErrorSummary as FormStepErrorSummary } from '../../forms/components/StepErrorSummary';

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
  return <FormTextField register={form.register} name={name} label={label} required={required} error={useError(form, name)} hint={hint} wide={wide} ltr={ltr} type={type} inputMode={inputMode} />;
}

/** A choice from a fixed list (shadcn Select). Value "" = nothing chosen. */
export function SelectField({ form, name, label, required, hint, options, optionLabel, placeholder }: FormProps & {
  name: FormField; label: string; required?: boolean; hint?: ReactNode; options: readonly string[]; optionLabel: (value: string) => string; placeholder: string;
}) {
  return <FormSelectField control={form.control} name={name} label={label} required={required} error={useError(form, name)} hint={hint}
    options={options} optionLabel={optionLabel} placeholder={placeholder} />;
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
  return <FormStepErrorSummary fields={stepFields[step] ?? []} focusKey={focusKey} entry={name => {
    const code = form.formState.errors[name]?.message, label = labels[name];
    return code && label ? { id: name, label, message: t(`onboarding.error.${errorCode(code)}`) } : undefined;
  }} />;
}
