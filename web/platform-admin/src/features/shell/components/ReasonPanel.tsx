import { useId, type FormEventHandler, type ReactNode } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, Card, Label, Textarea } from '@medvya/ui';

type Props = {
  title: string;
  /** What will happen, in words, above the reason. */
  children?: ReactNode;
  form: UseFormReturn<{ reason: string }>;
  confirmLabel: string;
  destructive?: boolean;
  busy: boolean;
  /** A message the server's answer needs to show (refused, failed), already translated. */
  problem?: string | null;
  onConfirm: FormEventHandler;
  onCancel: () => void;
};

/** Every console action asks for a reason, saved with the action in the platform audit log (MDV-P5-040). */
export default function ReasonPanel({ title, children, form, confirmLabel, destructive, busy, problem, onConfirm, onCancel }: Props) {
  const { t } = useI18n();
  const id = useId();
  const error = form.formState.errors.reason?.message;
  return (
    <Card title={title}>
      {children}
      <form onSubmit={onConfirm} noValidate className="flex flex-col gap-3">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor={id}>{t('platform.reason.label')}</Label>
          <Textarea id={id} rows={2} aria-invalid={error ? true : undefined} aria-describedby={`${id}-hint`} {...form.register('reason')} />
          <p id={`${id}-hint`} className={error ? 'text-sm font-semibold text-danger-700' : 'text-sm text-muted-foreground'}>
            {error ? t(`platform.error.${error}` as MessageKey) : t('platform.reason.hint')}
          </p>
        </div>
        {problem && <p role="alert" className="text-sm font-semibold text-danger-700">{problem}</p>}
        <div className="flex flex-wrap gap-2">
          <Button type="submit" variant={destructive ? 'destructive' : 'default'} loading={busy}>{confirmLabel}</Button>
          <Button type="button" variant="outline" onClick={onCancel}>{t('platform.action.cancel')}</Button>
        </div>
      </form>
    </Card>
  );
}
