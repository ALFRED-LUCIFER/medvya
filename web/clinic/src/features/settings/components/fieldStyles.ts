export const fieldError = 'text-sm font-semibold text-danger-700';

/** A StickyActionBar at the foot of a settings card (MDV-P2-064): edge to edge in the card, in the card's colour, pinned while the card scrolls. */
export const cardBar = '-mx-4 -mb-4 rounded-b-lg bg-card';

/** aria wiring for a control inside <Field>. */
export const describedBy = (id: string, invalid: boolean, hint = false) =>
  ({ 'aria-invalid': invalid || undefined, 'aria-describedby': [hint ? `${id}-hint` : '', invalid ? `${id}-error` : ''].filter(Boolean).join(' ') || undefined });
