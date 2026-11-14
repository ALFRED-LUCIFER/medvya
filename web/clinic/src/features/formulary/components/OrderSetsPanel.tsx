import type { FormEventHandler } from 'react';
import type { FieldError, UseFormReturn } from 'react-hook-form';
import { CircleCheck } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Field, Input, Table, TableBody, TableCell, TableHead, TableHeader, TablePagination, TableRow, TableState, Textarea } from '@medvya/ui';
import { fieldError } from '../../settings/components/fieldStyles';
import { Section } from '../../settings/components/Section';
import type { TableView } from '../../table/domain/tableSort';
import type { OrderSet, OrderSetForm, ProblemCode } from '../domain/formulary';

type Props = {
  sets: OrderSet[];
  table: TableView<OrderSet, 'name'>;
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
          {/* Compact: a reference list read with a mouse (DESIGN.md, Density). */}
          <Table label={t('orderSets.tableLabel')} density="compact" stacked status={p.table.status} className="md:min-w-[480px]">
            <TableHeader>
              <TableRow>
                <TableHead scope="col" sort={p.table.sortOf('name')} onSort={() => p.table.onSort('name')}>{t('orderSets.col.name')}</TableHead>
                <TableHead scope="col">{t('orderSets.col.items')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {p.table.rows.map(s => (
                <TableRow key={s.id}><TableHead scope="row">{s.name}</TableHead><TableCell label={t('orderSets.col.items')}>{s.items.join(', ')}</TableCell></TableRow>
              ))}
            </TableBody>
          </Table>
          <TablePagination page={p.table.page} pageCount={p.table.pageCount} onPage={p.table.onPage} />
        </TableState>
      </Section>

      <Section title={t('orderSets.add.title')}>
        <form noValidate onSubmit={p.onSubmit} className="flex flex-col gap-4">
          <Field id="set-name" label={t('orderSets.field.name')} error={problem(errors.name)}>
            <Input autoComplete="off" {...p.form.register('name')} />
          </Field>
          <Field id="set-items" label={t('orderSets.field.items')} error={problem(errors.items)}>
            <Textarea rows={5} {...p.form.register('items')} />
          </Field>
          {p.formError && <p role="alert" className={fieldError}>{t(`formulary.error.${p.formError}`)}</p>}
          {p.saved && <p role="status" className="flex items-center gap-2 font-bold"><CircleCheck aria-hidden className="size-5" />{t('orderSets.add.saved')}</p>}
          <Button type="submit" className="self-start" loading={p.saving}>{t('orderSets.add.submit')}</Button>
        </form>
      </Section>
    </div>
  );
}
