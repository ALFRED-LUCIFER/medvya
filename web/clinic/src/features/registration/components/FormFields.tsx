import type { ReactNode } from 'react';
import type { Control, FieldErrors, UseFormRegister } from 'react-hook-form';
import { CircleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { ChoiceRow } from '@medvya/ui';
import type { Country } from '@medvya/i18n';
import { errorCode, fieldLabel, sexes, type FormField, type RegistrationForm } from '../domain/registration';
import { TextField as FormTextField, type TextFieldProps } from '../../forms/components/TextField';
import { SelectField as FormSelectField } from '../../forms/components/SelectField';
import { StepErrorSummary } from '../../forms/components/StepErrorSummary';

type FormProps = { register: UseFormRegister<RegistrationForm>; control: Control<RegistrationForm>; errors: FieldErrors<RegistrationForm> };

/** The translated message for a field's error code (client schema or server), or undefined when the field is valid. */
function useFieldError(f: FormProps, name: FormField): string | undefined {
  const { t } = useI18n();
  const code = f.errors[name]?.message;
  return code ? t(`registration.error.${errorCode(code)}`) : undefined;
}

/** After a failed submit: each field in error with its label, linked to the field; it takes focus when focusKey changes. */
export function FormErrorSummary({ f, summary, country, values, quick }: {
  f: FormProps; summary: { focusKey: string; fields: FormField[] }; country: Country; values: RegistrationForm; quick: boolean;
}) {
  const { t } = useI18n();
  return <StepErrorSummary fields={summary.fields} focusKey={summary.focusKey} entry={field => ({
    id: field, label: t(fieldLabel(field, country, values, quick)), message: t(`registration.error.${errorCode(f.errors[field]?.message)}`),
  })} />;
}

/** A text input bound to one form field; LTR for numbers and codes so they read the same in Arabic. */
export function TextField({ f, name, ...props }: { f: FormProps; name: FormField } & Omit<TextFieldProps<RegistrationForm>, 'register' | 'name' | 'error'>) {
  return <FormTextField register={f.register} name={name} error={useFieldError(f, name)} {...props} />;
}

/** A choice from a fixed list (shadcn Select). Value "" = nothing chosen. */
export function SelectField<T extends string>({ f, name, label, required, hint, options, optionLabel, placeholder, wide }: {
  f: FormProps; name: FormField; label: string; required?: boolean; hint?: ReactNode; options: readonly T[];
  optionLabel: (value: T) => string; placeholder: string; wide?: boolean;
}) {
  return <FormSelectField control={f.control} name={name} error={useFieldError(f, name)} label={label} required={required} hint={hint} options={options}
    optionLabel={optionLabel} placeholder={placeholder} wide={wide} />;
}

/** Radio buttons, so a fieldset rather than a Field; the same words and error styling as Field. The first radio carries the id the error summary links to. */
export function SexField({ f }: { f: FormProps }) {
  const { t } = useI18n();
  const error = useFieldError(f, 'sexAtBirth');
  return (
    <fieldset aria-describedby={error ? 'sexAtBirth-error' : undefined} className="flex min-w-0 flex-col gap-1.5">
      <legend className="mb-1.5 text-label">
        {t('registration.field.sexAtBirth')} <span className="font-normal text-muted-foreground">{t('field.required')}</span>
      </legend>
      <div className="flex flex-wrap gap-2">
        {sexes.map((s, i) => (
          <ChoiceRow key={s} className="gap-2 rounded-md border border-input px-3 has-[:checked]:border-brand-700 has-[:checked]:bg-accent" id={i === 0 ? 'sexAtBirth' : undefined} type="radio" value={s} {...f.register('sexAtBirth')} aria-invalid={error ? true : undefined}>
            {t(`patients.sex.${s}`)}
          </ChoiceRow>
        ))}
      </div>
      {error && (
        <p id="sexAtBirth-error" className="flex items-start gap-1.5 text-label text-danger-text">
          <CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />{error}
        </p>
      )}
    </fieldset>
  );
}

export function Checkbox({ f, name, label }: { f: FormProps; name: 'ageInstead'; label: string }) {
  return (
    <ChoiceRow className="gap-2 self-start" type="checkbox" {...f.register(name)}>
      {label}
    </ChoiceRow>
  );
}

export type { FormProps };
