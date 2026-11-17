import { useI18n } from '@medvya/i18n';
import { ErrorSummary } from '@medvya/ui';

type Entry = { id: string; label: string; message: string };

/**
 * After a failed Next or submit: the step's fields in error, each linked to its control by id, in field order.
 * `entry` returns undefined for a field that is valid (or has no label); the summary takes focus when focusKey changes.
 */
export function StepErrorSummary<N extends string>({ fields, entry, focusKey, className }: {
  fields: readonly N[]; entry: (name: N) => Entry | undefined; focusKey: number | string; className?: string;
}) {
  const { t } = useI18n();
  const errors = fields.flatMap(name => entry(name) ?? []);
  return <ErrorSummary title={t('field.errorSummary')} focusKey={focusKey} errors={errors} className={className} />;
}
