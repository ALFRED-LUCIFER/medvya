import { useId, type FormEventHandler, type ReactNode } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, Card, ErrorSummary, Field, Textarea } from '@medvya/ui';

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
  const code = form.formState.errors.reason?.message;
  const error = code ? t(`platform.error.${code}` as MessageKey) : undefined;
  return (
    <Card title={title}>
      {children}
      <form onSubmit={onConfirm} noValidate className="flex flex-col gap-3">
        <ErrorSummary title={t('field.errorSummary')} focusKey={form.formState.submitCount}
          errors={error ? [{ id, label: t('platform.reason.label'), message: error }] : []} />
        <Field id={id} label={t('platform.reason.label')} required hint={t('platform.reason.hint')} error={error}>
          <Textarea rows={2} {...form.register('reason')} />
        </Field>
        {problem && <p role="alert" className="text-sm font-semibold text-danger-700">{problem}</p>}
        <div className="flex flex-wrap gap-2">
          <Button type="submit" variant={destructive ? 'destructive' : 'default'} loading={busy}>{confirmLabel}</Button>
          <Button type="button" variant="outline" onClick={onCancel}>{t('platform.action.cancel')}</Button>
        </div>
      </form>
    </Card>
  );
}
