import type { ReactNode } from 'react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { CircleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Input, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { errorCode, type FormField, type WizardForm } from '../domain/onboarding';

export type FormProps = { form: UseFormReturn<WizardForm> };

/** A field-level error under its input; the wizard's live region also announces that something needs fixing. */
export function FieldError({ id, code }: { id: string; code: string | undefined }) {
  const { t } = useI18n();
  if (!code) return null;
  return (
    <p id={id} className="flex items-center gap-1.5 text-sm font-semibold text-danger-700">
      <CircleAlert className="size-4 shrink-0" aria-hidden />{t(`onboarding.error.${errorCode(code)}`)}
    </p>
  );
}

type InputA11y = { id: string; 'aria-invalid'?: true; 'aria-describedby'?: string; 'aria-required'?: true };

export function Field({ id, label, required = false, optional = false, error, hint, wide = false, children }: {
  id: string; label: string; required?: boolean; optional?: boolean; error?: string; hint?: ReactNode; wide?: boolean; children: (a: InputA11y) => ReactNode;
}) {
  const { t } = useI18n();
  const described = [hint && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(' ') || undefined;
  return (
    <div className={wide ? 'flex flex-col gap-1.5 md:col-span-2' : 'flex flex-col gap-1.5'}>
      <Label htmlFor={id}>
        {label}
        {required && <span className="text-danger-700" aria-hidden> {t('registration.requiredMark')}</span>}
        {optional && <span className="font-normal text-muted-foreground"> {t('onboarding.optional')}</span>}
      </Label>
      {children({ id, 'aria-invalid': error ? true : undefined, 'aria-describedby': described, 'aria-required': required || undefined })}
      {hint && <p id={`${id}-hint`} className="text-xs text-muted-foreground">{hint}</p>}
      <FieldError id={`${id}-error`} code={error} />
    </div>
  );
}

export function TextField({ form, name, label, required, optional, hint, ltr, wide, type = 'text', inputMode }: FormProps & {
  name: FormField; label: string; required?: boolean; optional?: boolean; hint?: ReactNode; ltr?: boolean; wide?: boolean;
  type?: 'text' | 'email' | 'time' | 'date'; inputMode?: 'numeric' | 'email';
}) {
  return (
    <Field id={name} label={label} required={required} optional={optional} error={form.formState.errors[name]?.message} hint={hint} wide={wide}>
      {a => <Input {...a} {...form.register(name)} type={type} inputMode={inputMode} dir={ltr ? 'ltr' : undefined} autoComplete="off" spellCheck={false} />}
    </Field>
  );
}

/** A choice from a fixed list (shadcn Select). Value "" = nothing chosen. */
export function SelectField({ form, name, label, required, hint, options, optionLabel, placeholder }: FormProps & {
  name: FormField; label: string; required?: boolean; hint?: ReactNode; options: readonly string[]; optionLabel: (value: string) => string; placeholder: string;
}) {
  return (
    <Field id={name} label={label} required={required} error={form.formState.errors[name]?.message} hint={hint}>
      {a => (
        <Controller control={form.control} name={name} render={({ field }) => (
          <Select value={field.value as string} onValueChange={field.onChange}>
            <SelectTrigger {...a} className="w-full" onBlur={field.onBlur}><SelectValue placeholder={placeholder} /></SelectTrigger>
            <SelectContent>{options.map(o => <SelectItem key={o} value={o}>{optionLabel(o)}</SelectItem>)}</SelectContent>
          </Select>
        )} />
      )}
    </Field>
  );
}
