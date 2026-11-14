export const fieldError = 'text-sm font-semibold text-danger-700';

/** A StickyActionBar at the foot of a settings card (MDV-P2-064): edge to edge in the card, in the card's colour, pinned while the card scrolls. */
export const cardBar = '-mx-4 -mb-4 md:-mb-4 rounded-b-lg bg-card';

/** aria wiring for a control inside <Field>. */
export const describedBy = (id: string, invalid: boolean, hint = false) =>
  ({ 'aria-invalid': invalid || undefined, 'aria-describedby': [hint ? `${id}-hint` : '', invalid ? `${id}-error` : ''].filter(Boolean).join(' ') || undefined });

/** A settings form's fields auto-fit at a 200 px minimum across the panel, as registration does (MDV-P2-063), so a wide screen is used. */
export const fieldGrid = 'grid grid-cols-[repeat(auto-fit,minmax(min(200px,100%),1fr))] gap-x-4 gap-y-3.5';
