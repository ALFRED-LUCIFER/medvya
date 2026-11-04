export const fieldError = 'text-sm font-semibold text-danger-700';

/** aria wiring for a control inside <Field>. */
export const describedBy = (id: string, invalid: boolean, hint = false) =>
  ({ 'aria-invalid': invalid || undefined, 'aria-describedby': [hint ? `${id}-hint` : '', invalid ? `${id}-error` : ''].filter(Boolean).join(' ') || undefined });
