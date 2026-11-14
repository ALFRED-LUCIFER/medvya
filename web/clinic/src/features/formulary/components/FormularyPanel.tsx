import type { FormEventHandler } from 'react';
import { Controller, type FieldError, type UseFormReturn } from 'react-hook-form';
import { CircleCheck, TriangleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Field, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
import { fieldError } from '../../settings/components/fieldStyles';
import { Section } from '../../settings/components/Section';
import type { TableView } from '../../table/domain/tableSort';
import { classesFor, FORMULARY_LIMIT, formularyColumns, forms, isFormularySortable, type Country, type FormularyColumn, type FormularyItem, type FormularySortColumn, type MedicineForm, type ProblemCode } from '../domain/formulary';

type Props = {
  country: Country;
  query: string;
  onQuery: (query: string) => void;
  items: FormularyItem[];
  table: TableView<FormularyItem, FormularySortColumn>;
  loading: boolean;
  failed: boolean;
  form: UseFormReturn<MedicineForm>;
  saving: boolean;
  saved: boolean;
  formError: ProblemCode | null;
  onSubmit: FormEventHandler<HTMLFormElement>;
};

/** Medicines & formulary (F-13): the country list, its classes and what each prints, and the form to add the clinic's own medicine. */
export default function FormularyPanel(p: Props) {
  const { t } = useI18n();
  const { errors } = p.form.formState;
  const tableState = p.loading ? 'loading' : p.failed ? 'error' : p.items.length === 0 ? 'empty' : 'ready';
  // A code the server sent is translated; anything the form itself rejected is a missing or malformed value.
  const problem = (e: FieldError | undefined) => (e ? t(`formulary.error.${(e.type === 'server' ? e.message : 'required') as ProblemCode}`) : null);
  const col = (c: FormularyColumn) => t(`formulary.col.${c}`);

  return (
    <div className="flex flex-col gap-4">
      <Section title={t('formulary.title')} intro={t('formulary.intro')}>
        <p role="note" className="flex items-start gap-2 rounded-md border border-warning-700 px-3 py-2 text-sm font-semibold"><TriangleAlert aria-hidden className="mt-0.5 size-4 shrink-0" />{t('formulary.starterNotice')}</p>
        <Field id="formulary-search" label={t('formulary.search')}>
          <Input type="search" autoComplete="off" value={p.query} onChange={e => p.onQuery(e.target.value)} />
        </Field>
        <TableState state={tableState} messages={{ loading: t('formulary.loading'), error: t('formulary.loadFailed'), empty: t('formulary.empty') }}>
          {/* Compact: a reference list read with a mouse (DESIGN.md, Density). No paging: the search answers at most 50 medicines. */}
          <Table label={t('formulary.tableLabel')} density="compact" stacked status={p.table.status} className="md:min-w-[760px]">
            <TableHeader>
              <TableRow>
                {formularyColumns.map(c => (
                  <TableHead key={c} scope="col" {...(isFormularySortable(c) && { sort: p.table.sortOf(c), onSort: () => p.table.onSort(c) })}>{col(c)}</TableHead>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {p.table.rows.map(i => (
                <TableRow key={i.id}>
                  <TableHead scope="row">{i.generic}</TableHead>
                  <TableCell label={col('strength')}><bdi dir="ltr">{i.strength}</bdi></TableCell>
                  <TableCell label={col('form')}>{t(`formulary.form.${i.form}`)}</TableCell>
                  <TableCell label={col('brands')}>{i.brands.join(', ')}</TableCell>
                  <TableCell label={col('class')}>
                    {t(`formulary.class.${i.regClass}`)}
                    {i.printRequirements.map(r => <span key={r} className="block text-sm text-muted-foreground">{t(`formulary.print.${r}`)}</span>)}
                  </TableCell>
                  <TableCell label={col('code')}>{i.nationalCode ? <bdi dir="ltr">{i.nationalCode}</bdi> : t('formulary.noCode')}</TableCell>
                  <TableCell label={col('source')}>{t(`formulary.source.${i.source}`)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          {p.items.length >= FORMULARY_LIMIT && <p className="text-sm">{t('formulary.capped', { count: FORMULARY_LIMIT })}</p>}
        </TableState>
      </Section>

      <Section title={t('formulary.add.title')} intro={t('formulary.add.intro')}>
        <form noValidate onSubmit={p.onSubmit} className="grid gap-4 sm:grid-cols-2">
          <Field id="med-generic" label={t('formulary.field.generic')} error={problem(errors.generic)}>
            <Input autoComplete="off" {...p.form.register('generic')} />
          </Field>
          <Field id="med-strength" label={t('formulary.field.strength')} error={problem(errors.strength)}>
            <Input dir="ltr" autoComplete="off" {...p.form.register('strength')} />
          </Field>
          <Controller control={p.form.control} name="form" render={({ field }) => (
            <Select value={field.value ?? ''} onValueChange={field.onChange}>
              <Field id="med-form" label={t('formulary.field.form')} error={problem(errors.form)}>
                <SelectTrigger className="w-full"><SelectValue placeholder={t('formulary.choose')} /></SelectTrigger>
              </Field>
              <SelectContent>{forms.map(f => <SelectItem key={f} value={f}>{t(`formulary.form.${f}`)}</SelectItem>)}</SelectContent>
            </Select>
          )} />
          <Controller control={p.form.control} name="regClass" render={({ field }) => (
            <Select value={field.value ?? ''} onValueChange={field.onChange}>
              <Field id="med-class" label={t('formulary.field.regClass')} hint={t('formulary.field.regClass.hint')} error={problem(errors.regClass)}>
                <SelectTrigger className="w-full"><SelectValue placeholder={t('formulary.choose')} /></SelectTrigger>
              </Field>
              <SelectContent>{classesFor(p.country).map(c => <SelectItem key={c} value={c}>{t(`formulary.class.${c}`)}</SelectItem>)}</SelectContent>
            </Select>
          )} />
          <Field id="med-brands" label={t('formulary.field.brands')} hint={t('formulary.field.brands.hint')} error={problem(errors.brands)} className="sm:col-span-2">
            <Input autoComplete="off" {...p.form.register('brands')} />
          </Field>
          <Field id="med-code" label={t('formulary.field.nationalCode')} error={problem(errors.nationalCode)}>
            <Input dir="ltr" autoComplete="off" {...p.form.register('nationalCode')} />
          </Field>
          <Field id="med-atc" label={t('formulary.field.atc')} error={problem(errors.atc)}>
            <Input dir="ltr" autoComplete="off" {...p.form.register('atc')} />
          </Field>
          <Field id="med-sig" label={t('formulary.field.defaultSig')} error={problem(errors.defaultSig)} className="sm:col-span-2">
            <Input autoComplete="off" {...p.form.register('defaultSig')} />
          </Field>
          <div className="flex flex-col gap-2 sm:col-span-2">
            {p.formError && <p role="alert" className={fieldError}>{t(`formulary.error.${p.formError}`)}</p>}
            {p.saved && <p role="status" className="flex items-center gap-2 font-bold"><CircleCheck aria-hidden className="size-5" />{t('formulary.add.saved')}</p>}
            <Button type="submit" className="self-start" loading={p.saving}>{t('formulary.add.submit')}</Button>
          </div>
        </form>
      </Section>
    </div>
  );
}
