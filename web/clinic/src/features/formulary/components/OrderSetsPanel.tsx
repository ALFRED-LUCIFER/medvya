import type { FormEventHandler } from 'react';
import type { FieldError, UseFormReturn } from 'react-hook-form';
import { CircleCheck } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Input, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState, Textarea } from '@medvya/ui';
import { describedBy, fieldError } from '../../settings/components/fieldStyles';
import { Field } from '../../settings/components/Field';
import { Section } from '../../settings/components/Section';
import type { OrderSet, OrderSetForm, ProblemCode } from '../domain/formulary';

type Props = {
  sets: OrderSet[];
  loading: boolean;
  failed: boolean;
  form: UseFormReturn<OrderSetForm>;
  saving: boolean;
  saved: boolean;
  formError: ProblemCode | null;
  onSubmit: FormEventHandler<HTMLFormElement>;
};

/** Order sets (F-13): named groups of tests a doctor orders together; the order composer offers them. */
export default function OrderSetsPanel(p: Props) {
  const { t } = useI18n();
  const { errors } = p.form.formState;
  const tableState = p.loading ? 'loading' : p.failed ? 'error' : p.sets.length === 0 ? 'empty' : 'ready';
  const problem = (e: FieldError | undefined) => (e ? t(`formulary.error.${(e.type === 'server' ? e.message : 'required') as ProblemCode}`) : null);

  return (
    <div className="flex flex-col gap-4">
      <Section title={t('orderSets.title')} intro={t('orderSets.intro')}>
        <TableState state={tableState} messages={{ loading: t('formulary.loading'), error: t('formulary.loadFailed'), empty: t('orderSets.empty') }}>
          <Table label={t('orderSets.tableLabel')} className="min-w-[480px]">
            <TableHeader>
              <TableRow><TableHead scope="col">{t('orderSets.col.name')}</TableHead><TableHead scope="col">{t('orderSets.col.items')}</TableHead></TableRow>
            </TableHeader>
            <TableBody>
              {p.sets.map(s => (
                <TableRow key={s.id}><TableHead scope="row">{s.name}</TableHead><TableCell>{s.items.join(', ')}</TableCell></TableRow>
              ))}
            </TableBody>
          </Table>
        </TableState>
      </Section>

      <Section title={t('orderSets.add.title')}>
        <form noValidate onSubmit={p.onSubmit} className="flex flex-col gap-4">
          <Field id="set-name" label={t('orderSets.field.name')} error={problem(errors.name)}>
            <Input id="set-name" className="h-11" autoComplete="off" {...describedBy('set-name', !!errors.name)} {...p.form.register('name')} />
          </Field>
          <Field id="set-items" label={t('orderSets.field.items')} error={problem(errors.items)}>
            <Textarea id="set-items" rows={5} {...describedBy('set-items', !!errors.items)} {...p.form.register('items')} />
          </Field>
          {p.formError && <p role="alert" className={fieldError}>{t(`formulary.error.${p.formError}`)}</p>}
          {p.saved && <p role="status" className="flex items-center gap-2 font-bold"><CircleCheck aria-hidden className="size-5" />{t('orderSets.add.saved')}</p>}
          <Button type="submit" className="self-start" loading={p.saving}>{t('orderSets.add.submit')}</Button>
        </form>
      </Section>
    </div>
  );
}
