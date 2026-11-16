import type { HTMLInputAutoCompleteAttribute, ReactNode } from 'react';
import type { FieldValues, Path, UseFormRegister } from 'react-hook-form';
import { Field, Input } from '@medvya/ui';

export type TextFieldProps<T extends FieldValues> = {
  register: UseFormRegister<T>; name: Path<T>;
  /** The control's id (the error summary links to it); defaults to the field name. */
  id?: string;
  label: string;
  /** Already translated: each form has its own error words. */
  error?: ReactNode;
  required?: boolean; hint?: ReactNode;
  /** Spans both columns of a two-column form grid. */
  wide?: boolean;
  /** Left-to-right for numbers, codes and emails, so they read the same in Arabic. */
  ltr?: boolean;
  /** Arabic text typed in an English screen (sets lang="ar" too). */
  dir?: 'rtl';
  type?: 'text' | 'tel' | 'email' | 'date' | 'time';
  inputMode?: 'numeric' | 'tel' | 'email';
  autoComplete?: HTMLInputAutoCompleteAttribute;
  autoFocus?: boolean;
};

/** A text input bound to one React Hook Form field, inside the library Field (label, mark, hint, error). */
export function TextField<T extends FieldValues>({ register, name, id = name, label, error, required, hint, wide, ltr, dir, type = 'text', inputMode, autoComplete = 'off', autoFocus }: TextFieldProps<T>) {
  return (
    <Field id={id} label={label} required={required} error={error} hint={hint} className={wide ? 'md:col-span-2' : undefined}>
      <Input {...register(name)} type={type} inputMode={inputMode} dir={dir ?? (ltr ? 'ltr' : undefined)} lang={dir === 'rtl' ? 'ar' : undefined}
        autoComplete={autoComplete} autoFocus={autoFocus} spellCheck={false} />
    </Field>
  );
}
