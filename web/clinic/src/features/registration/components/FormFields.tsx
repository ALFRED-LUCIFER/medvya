import type { ReactNode } from 'react';
import { Controller, type Control, type FieldErrors, type UseFormRegister } from 'react-hook-form';
import { CircleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Input, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { errorCode, sexes, type FormField, type RegistrationForm } from '../domain/registration';

export const Required = () => {
  const { t } = useI18n();
  return <span className="text-danger-700" aria-hidden>{t('registration.requiredMark')}</span>;
};

export function FieldError({ id, code }: { id: string; code: string | undefined }) {
  const { t } = useI18n();
  if (!code) return null;
  return (
    <p id={id} className="flex items-center gap-1.5 text-sm font-semibold text-danger-700">
      <CircleAlert className="size-4 shrink-0" aria-hidden />{t(`registration.error.${errorCode(code)}`)}
    </p>
  );
}

type InputA11y = { id: string; 'aria-invalid'?: true; 'aria-describedby'?: string; 'aria-required'?: true; className: string };

export function Field({ id, label, required = false, error, hint, wide = false, children }: {
  id: string; label: string; required?: boolean; error?: string; hint?: ReactNode; wide?: boolean; children: (a: InputA11y) => ReactNode;
}) {
  const described = [hint && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(' ') || undefined;
  return (
    <div className={wide ? 'flex flex-col gap-1.5 md:col-span-2' : 'flex flex-col gap-1.5'}>
      <Label htmlFor={id}>{label} {required && <Required />}</Label>
      {children({ id, 'aria-invalid': error ? true : undefined, 'aria-describedby': described, 'aria-required': required || undefined, className: 'min-h-11' })}
      {hint && <p id={`${id}-hint`} className="text-xs text-muted-foreground">{hint}</p>}
      <FieldError id={`${id}-error`} code={error} />
    </div>
  );
}

type FormProps = { register: UseFormRegister<RegistrationForm>; control: Control<RegistrationForm>; errors: FieldErrors<RegistrationForm> };

/** A text input bound to one form field; LTR for numbers and codes so they read the same in Arabic. */
export function TextField({ f, name, label, required, hint, ltr, wide, type = 'text', inputMode, dir }: {
  f: FormProps; name: FormField; label: string; required?: boolean; hint?: ReactNode; ltr?: boolean; wide?: boolean;
  type?: 'text' | 'tel' | 'email' | 'date'; inputMode?: 'numeric' | 'tel' | 'email'; dir?: 'rtl';
}) {
  return (
    <Field id={name} label={label} required={required} error={f.errors[name]?.message} hint={hint} wide={wide}>
      {a => <Input {...a} {...f.register(name)} type={type} inputMode={inputMode} dir={dir ?? (ltr ? 'ltr' : undefined)}
        lang={dir === 'rtl' ? 'ar' : undefined} autoComplete="off" spellCheck={false} />}
    </Field>
  );
}

/** A choice from a fixed list (shadcn Select). Value "" = nothing chosen. */
export function SelectField<T extends string>({ f, name, label, required, hint, options, optionLabel, placeholder, wide }: {
  f: FormProps; name: FormField; label: string; required?: boolean; hint?: ReactNode; options: readonly T[];
  optionLabel: (value: T) => string; placeholder: string; wide?: boolean;
}) {
  const error = f.errors[name]?.message;
  return (
    <Field id={name} label={label} required={required} error={error} hint={hint} wide={wide}>
      {a => (
        <Controller control={f.control} name={name} render={({ field }) => (
          <Select value={field.value as string} onValueChange={field.onChange}>
            <SelectTrigger {...a} className="min-h-11 w-full" onBlur={field.onBlur}><SelectValue placeholder={placeholder} /></SelectTrigger>
            <SelectContent>{options.map(o => <SelectItem key={o} value={o}>{optionLabel(o)}</SelectItem>)}</SelectContent>
          </Select>
        )} />
      )}
    </Field>
  );
}

export function SexField({ f }: { f: FormProps }) {
  const { t } = useI18n();
  const error = f.errors.sexAtBirth?.message;
  return (
    <fieldset aria-describedby={error ? 'sexAtBirth-error' : undefined} className="flex flex-col gap-2">
      <legend className="mb-2 text-sm font-bold">{t('registration.field.sexAtBirth')} <Required /></legend>
      <div className="flex flex-wrap gap-2">
        {sexes.map(s => (
          <label key={s} className="flex min-h-11 cursor-pointer items-center gap-2 rounded-md border border-input px-3 has-[:checked]:border-brand-700 has-[:checked]:bg-accent">
            <input type="radio" value={s} {...f.register('sexAtBirth')} aria-invalid={error ? true : undefined} className="size-4 accent-brand-700" />
            {t(`patients.sex.${s}`)}
          </label>
        ))}
      </div>
      <FieldError id="sexAtBirth-error" code={error} />
    </fieldset>
  );
}

export function Checkbox({ f, name, label }: { f: FormProps; name: 'ageInstead'; label: string }) {
  return (
    <label className="flex min-h-11 cursor-pointer items-center gap-2 self-start">
      <input type="checkbox" {...f.register(name)} className="size-4 accent-brand-700" />
      {label}
    </label>
  );
}

export type { FormProps };
