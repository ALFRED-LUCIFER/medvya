import type { ReactNode } from 'react';
import { Controller, type Control, type FieldValues, type Path } from 'react-hook-form';
import { Field, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';

export type SelectFieldProps<T extends FieldValues, O extends string> = {
  control: Control<T>; name: Path<T>;
  /** The trigger's id (the error summary links to it); defaults to the field name. */
  id?: string;
  label: string;
  /** Already translated: each form has its own error words. */
  error?: ReactNode;
  required?: boolean; hint?: ReactNode; wide?: boolean;
  options: readonly O[]; optionLabel: (value: O) => string; placeholder?: string;
};

/** A choice from a fixed list (library Select) bound to one React Hook Form field. Value "" = nothing chosen. */
export function SelectField<T extends FieldValues, O extends string>({ control, name, id = name, label, error, required, hint, wide, options, optionLabel, placeholder }: SelectFieldProps<T, O>) {
  return (
    <Controller control={control} name={name} render={({ field }) => (
      <Select value={(field.value as string | undefined) ?? ''} onValueChange={field.onChange}>
        <Field id={id} label={label} required={required} error={error} hint={hint} className={wide ? 'md:col-span-2' : undefined}>
          <SelectTrigger className="w-full" onBlur={field.onBlur}><SelectValue placeholder={placeholder} /></SelectTrigger>
        </Field>
        <SelectContent>{options.map(o => <SelectItem key={o} value={o}>{optionLabel(o)}</SelectItem>)}</SelectContent>
      </Select>
    )} />
  );
}
