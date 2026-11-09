import type { FormEventHandler } from 'react';
import { Controller, type FieldError, type UseFormReturn } from 'react-hook-form';
import { CircleCheck, TriangleAlert } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
import { describedBy, fieldError } from '../../settings/components/fieldStyles';
import { Field } from '../../settings/components/Field';
import { Section } from '../../settings/components/Section';
import { classesFor, formularyColumns, forms, type Country, type FormularyItem, type MedicineForm, type ProblemCode } from '../domain/formulary';

type Props = {
  country: Country;
  query: string;
  onQuery: (query: string) => void;
  items: FormularyItem[];
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

  return (
    <div className="flex flex-col gap-4">
      <Section title={t('formulary.title')} intro={t('formulary.intro')}>
        <p role="note" className="flex items-start gap-2 rounded-md border border-warning-700 px-3 py-2 text-sm font-semibold"><TriangleAlert aria-hidden className="mt-0.5 size-4 shrink-0" />{t('formulary.starterNotice')}</p>
        <Field id="formulary-search" label={t('formulary.search')}>
          <Input id="formulary-search" type="search" className="h-11" autoComplete="off" value={p.query} onChange={e => p.onQuery(e.target.value)} />
        </Field>
        <TableState state={tableState} messages={{ loading: t('formulary.loading'), error: t('formulary.loadFailed'), empty: t('formulary.empty') }}>
          <Table label={t('formulary.tableLabel')} className="min-w-[760px]">
            <TableHeader>
              <TableRow>{formularyColumns.map(c => <TableHead key={c} scope="col">{t(`formulary.col.${c}`)}</TableHead>)}</TableRow>
            </TableHeader>
            <TableBody>
              {p.items.map(i => (
                <TableRow key={i.id}>
                  <TableHead scope="row">{i.generic}</TableHead>
                  <TableCell><bdi dir="ltr">{i.strength}</bdi></TableCell>
                  <TableCell>{t(`formulary.form.${i.form}`)}</TableCell>
                  <TableCell>{i.brands.join(', ')}</TableCell>
                  <TableCell>
                    {t(`formulary.class.${i.regClass}`)}
                    {i.printRequirements.map(r => <span key={r} className="block text-sm text-muted-foreground">{t(`formulary.print.${r}`)}</span>)}
                  </TableCell>
                  <TableCell>{i.nationalCode ? <bdi dir="ltr">{i.nationalCode}</bdi> : t('formulary.noCode')}</TableCell>
                  <TableCell>{t(`formulary.source.${i.source}`)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableState>
      </Section>

      <Section title={t('formulary.add.title')} intro={t('formulary.add.intro')}>
        <form noValidate onSubmit={p.onSubmit} className="grid gap-4 sm:grid-cols-2">
          <Field id="med-generic" label={t('formulary.field.generic')} error={problem(errors.generic)}>
            <Input id="med-generic" className="h-11" autoComplete="off" {...describedBy('med-generic', !!errors.generic)} {...p.form.register('generic')} />
          </Field>
          <Field id="med-strength" label={t('formulary.field.strength')} error={problem(errors.strength)}>
            <Input id="med-strength" dir="ltr" className="h-11" autoComplete="off" {...describedBy('med-strength', !!errors.strength)} {...p.form.register('strength')} />
          </Field>
          <Field id="med-form" label={t('formulary.field.form')} error={problem(errors.form)}>
            <Controller control={p.form.control} name="form" render={({ field }) => (
              <Select value={field.value ?? ''} onValueChange={field.onChange}>
                <SelectTrigger id="med-form" className="h-11 w-full" {...describedBy('med-form', !!errors.form)}><SelectValue placeholder={t('formulary.choose')} /></SelectTrigger>
                <SelectContent>{forms.map(f => <SelectItem key={f} value={f}>{t(`formulary.form.${f}`)}</SelectItem>)}</SelectContent>
              </Select>
            )} />
          </Field>
          <Field id="med-class" label={t('formulary.field.regClass')} hint={t('formulary.field.regClass.hint')} error={problem(errors.regClass)}>
            <Controller control={p.form.control} name="regClass" render={({ field }) => (
              <Select value={field.value ?? ''} onValueChange={field.onChange}>
                <SelectTrigger id="med-class" className="h-11 w-full" {...describedBy('med-class', !!errors.regClass, true)}><SelectValue placeholder={t('formulary.choose')} /></SelectTrigger>
                <SelectContent>{classesFor(p.country).map(c => <SelectItem key={c} value={c}>{t(`formulary.class.${c}`)}</SelectItem>)}</SelectContent>
              </Select>
            )} />
          </Field>
          <Field id="med-brands" label={t('formulary.field.brands')} hint={t('formulary.field.brands.hint')} error={problem(errors.brands)} className="sm:col-span-2">
            <Input id="med-brands" className="h-11" autoComplete="off" {...describedBy('med-brands', !!errors.brands, true)} {...p.form.register('brands')} />
          </Field>
          <Field id="med-code" label={t('formulary.field.nationalCode')} error={problem(errors.nationalCode)}>
            <Input id="med-code" dir="ltr" className="h-11" autoComplete="off" {...describedBy('med-code', !!errors.nationalCode)} {...p.form.register('nationalCode')} />
          </Field>
          <Field id="med-atc" label={t('formulary.field.atc')} error={problem(errors.atc)}>
            <Input id="med-atc" dir="ltr" className="h-11" autoComplete="off" {...describedBy('med-atc', !!errors.atc)} {...p.form.register('atc')} />
          </Field>
          <Field id="med-sig" label={t('formulary.field.defaultSig')} error={problem(errors.defaultSig)} className="sm:col-span-2">
            <Input id="med-sig" className="h-11" autoComplete="off" {...describedBy('med-sig', !!errors.defaultSig)} {...p.form.register('defaultSig')} />
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
