import { useId, type FormEventHandler, type ReactNode } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, Card, ErrorSummary, Field, StickyActionBar, Textarea } from '@medvya/ui';

/** One reason form is open at a time, so its bar's submit button finds it by this id. */
const formId = 'reason-form';

type Props = {
  title: string;
  /** What will happen, in words, above the reason. */
  children?: ReactNode;
  form: UseFormReturn<{ reason: string }>;
  onConfirm: FormEventHandler;
};

type BarProps = {
  confirmLabel: string;
  destructive?: boolean;
  busy: boolean;
  /** A message the server's answer needs to show (refused, failed), already translated. */
  problem?: string | null;
  onCancel: () => void;
};

/** Every console action asks for a reason, saved with the action in the platform audit log (MDV-P5-040). Its Cancel and Confirm are in ReasonBar. */
export default function ReasonPanel({ title, children, form, onConfirm }: Props) {
  const { t } = useI18n();
  const id = useId();
  const code = form.formState.errors.reason?.message;
  const error = code ? t(`platform.error.${code}` as MessageKey) : undefined;
  return (
    <Card title={title}>
      {children}
      <form id={formId} onSubmit={onConfirm} noValidate className="flex flex-col gap-3">
        <ErrorSummary title={t('field.errorSummary')} focusKey={form.formState.submitCount}
          errors={error ? [{ id, label: t('platform.reason.label'), message: error }] : []} />
        <Field id={id} label={t('platform.reason.label')} required hint={t('platform.reason.hint')} error={error}>
          <Textarea rows={2} {...form.register('reason')} />
        </Field>
      </form>
    </Card>
  );
}

/** The open reason's actions, in the page's bottom bar in place of its usual ones (DESIGN.md Screen layout standard, MDV-P2-065). */
export function ReasonBar({ confirmLabel, destructive, busy, problem, onCancel }: BarProps) {
  const { t } = useI18n();
  return (
    <StickyActionBar status={problem && <p role="alert" className="text-sm font-semibold text-danger-700">{problem}</p>}>
      <Button type="button" variant="outline" onClick={onCancel}>{t('platform.action.cancel')}</Button>
      <Button type="submit" form={formId} variant={destructive ? 'destructive' : 'default'} loading={busy}>{confirmLabel}</Button>
    </StickyActionBar>
  );
}
