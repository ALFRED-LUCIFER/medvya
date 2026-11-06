import type { ReactNode } from 'react';
import { Label } from '@medvya/ui';
import { fieldError } from './fieldStyles';

/** A label above its control, with the error (if any) announced and tied to the control through `id-error`. */
export function Field({ id, label, error, hint, className = '', children }: { id: string; label: string; error?: string | null; hint?: string; className?: string; children: ReactNode }) {
  return (
    <div className={`flex min-w-0 flex-col gap-2 ${className}`}>
      <Label htmlFor={id}>{label}</Label>
      {children}
      {hint && <p id={`${id}-hint`} className="text-sm text-muted-foreground">{hint}</p>}
      {error && <p id={`${id}-error`} role="alert" className={fieldError}>{error}</p>}
    </div>
  );
}
