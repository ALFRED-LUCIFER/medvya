import type { FormEventHandler } from 'react';
import { Controller, type FieldError, type UseFormReturn } from 'react-hook-form';
import { CircleCheck } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import {
  Button, ChoiceRow, Field, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Sheet, SheetContent, SheetTitle, StickyActionBar,
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState,
} from '@medvya/ui';
import { cardBar, fieldError, fieldGrid } from '../../settings/components/fieldStyles';
import { Section } from '../../settings/components/Section';
import type { TableView } from '../../table/domain/tableSort';
import {
  isServiceSortable, linePreview, serviceCategories, serviceColumns, taxTreatments,
  type CatalogueProblem, type Currency, type PriceForm, type PriceRow, type Service, type ServiceColumn, type ServiceForm, type ServiceSortColumn,
} from '../domain/catalogue';

type Props = {
  archived: boolean; onArchived: (archived: boolean) => void;
  services: Service[]; table: TableView<Service, ServiceSortColumn>; loading: boolean; failed: boolean;
  editing: 'new' | string | null; service: Service | null; onOpen: (id: 'new' | string) => void; onClose: () => void;
  form: UseFormReturn<ServiceForm>; formError: CatalogueProblem | null; saving: boolean; saved: boolean; onSave: FormEventHandler<HTMLFormElement>;
  archiving: boolean; onArchive: () => void;
  rows: PriceRow[]; pricesLoading: boolean; pricesFailed: boolean;
  priceForm: UseFormReturn<PriceForm>; priceDoctor: string | null; onPriceDoctor: (doctorId: string | null) => void; priceError: CatalogueProblem | null;
  pricing: boolean; today: string; onAddPrice: FormEventHandler<HTMLFormElement>;
};

/** Settings, Services (MDV-P4-002): the catalogue a bill is priced from, and a drawer to edit one service and its prices per doctor. */
export default function ServicesPanel(p: Props) {
  const { t, formatMoney, formatDate } = useI18n();
  const state = p.loading ? 'loading' : p.failed ? 'error' : p.services.length === 0 ? 'empty' : 'ready';
  const col = (c: ServiceColumn) => t(`billing.services.col.${c}`);
  const tax = (s: Pick<Service, 'taxTreatment' | 'gstRateBasisPoints'>) =>
    s.taxTreatment === 'Exempt' ? t('billing.tax.Exempt') : t('billing.tax.rate', { rate: (s.gstRateBasisPoints ?? 0) / 100 });
  const money = (minor: number | null, currency: Currency) => (minor === null ? t('billing.services.noPrice') : formatMoney(minor, currency));

  return (
    <Section title={t('billing.services.title')} intro={t('billing.services.intro')}
      actions={<Button variant="outline" onClick={() => p.onOpen('new')}>{t('billing.services.add')}</Button>}>
      <ChoiceRow type="checkbox" checked={p.archived} onChange={e => p.onArchived(e.target.checked)}>{t('billing.services.showArchived')}</ChoiceRow>
      <TableState state={state} messages={{ loading: t('billing.services.loading'), error: t('billing.services.loadFailed'), empty: t('billing.services.empty') }}>
        <Table label={t('billing.services.tableLabel')} stacked status={p.table.status}>
          <TableHeader>
            <TableRow>
              {serviceColumns.map(c => (
                <TableHead key={c} scope="col" {...(isServiceSortable(c) && { sort: p.table.sortOf(c), onSort: () => p.table.onSort(c) })}>{col(c)}</TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {p.table.rows.map(s => (
              <TableRow key={s.id}>
                <TableHead scope="row">
                  <Button variant="link" className="h-auto p-0 text-start" onClick={() => p.onOpen(s.id)}>{s.name}</Button>
                  {s.archivedAt && <span className="block text-sm text-muted-foreground">{t('billing.services.archived')}</span>}
                </TableHead>
                <TableCell label={col('category')}>{t(`billing.category.${s.category}`)}</TableCell>
                <TableCell label={col('sac')}>{s.sac ? <bdi dir="ltr">{s.sac}</bdi> : t('billing.services.none')}</TableCell>
                <TableCell label={col('tax')}>{tax(s)}</TableCell>
                <TableCell label={col('price')}>{money(s.clinicPriceMinor, s.currency)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableState>
      <Sheet open={p.editing !== null} onOpenChange={o => { if (!o) p.onClose(); }}>
        {p.editing !== null && <ServiceEditor p={p} tax={tax} money={money} formatDate={formatDate} />}
      </Sheet>
    </Section>
  );
}

function ServiceEditor({ p, tax, money, formatDate }: {
  p: Props;
  tax: (s: Pick<Service, 'taxTreatment' | 'gstRateBasisPoints'>) => string;
  money: (minor: number | null, currency: Currency) => string;
  formatDate: (d: Date) => string;
}) {
  const { t } = useI18n();
  const { errors } = p.form.formState;
  const problem = (e: FieldError | undefined) => (e ? t(`billing.error.${e.type === 'server' ? e.message : 'invalid'}` as MessageKey) : null);
  const values = p.form.watch();
  const taxable = values.taxTreatment === 'Taxable';
  const currency = p.service?.currency ?? 'INR';
  const previewMinor = p.rows[0]?.current?.amountMinor ?? null;
  const preview = previewMinor === null ? null
    : linePreview({ name: values.name, sac: values.sac || null, taxTreatment: values.taxTreatment, gstRateBasisPoints: taxable ? Math.round(Number(values.gstRate) * 100) : null }, previewMinor);
  const rowName = (r: (typeof p.rows)[number]) => (r.doctorId === null ? t('billing.prices.clinicDefault') : t('billing.prices.doctorAt', { name: r.name ?? '', location: r.location ?? '' }));

  return (
    <SheetContent closeLabel={t('billing.services.close')} aria-describedby={undefined}>
      <SheetTitle>{p.service ? p.service.name : t('billing.services.new')}</SheetTitle>
      <form id="service-form" noValidate onSubmit={p.onSave} className={fieldGrid}>
        <Field id="service-name" label={t('billing.field.name')} error={problem(errors.name)}>
          <Input autoComplete="off" {...p.form.register('name')} />
        </Field>
        <Controller control={p.form.control} name="category" render={({ field }) => (
          <Select value={field.value} onValueChange={field.onChange}>
            <Field id="service-category" label={t('billing.field.category')} error={problem(errors.category)}>
              <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
            </Field>
            <SelectContent>{serviceCategories.map(c => <SelectItem key={c} value={c}>{t(`billing.category.${c}`)}</SelectItem>)}</SelectContent>
          </Select>
        )} />
        <Field id="service-code" label={t('billing.field.code')} error={problem(errors.code)}>
          <Input dir="ltr" autoComplete="off" {...p.form.register('code')} />
        </Field>
        <Field id="service-sac" label={t('billing.field.sac')} hint={t('billing.field.sac.hint')} error={problem(errors.sac)}>
          <Input dir="ltr" inputMode="numeric" autoComplete="off" {...p.form.register('sac')} />
        </Field>
        <Controller control={p.form.control} name="taxTreatment" render={({ field }) => (
          <Select value={field.value} onValueChange={field.onChange}>
            <Field id="service-tax" label={t('billing.field.tax')} error={problem(errors.taxTreatment)}>
              <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
            </Field>
            <SelectContent>{taxTreatments.map(c => <SelectItem key={c} value={c}>{t(`billing.tax.${c}`)}</SelectItem>)}</SelectContent>
          </Select>
        )} />
        {taxable && (
          <Field id="service-gst" label={t('billing.field.gstRate')} unit="%" error={problem(errors.gstRate)}>
            <Input dir="ltr" inputMode="decimal" autoComplete="off" {...p.form.register('gstRate')} />
          </Field>
        )}
        <Field id="service-duration" label={t('billing.field.duration')} unit={t('billing.field.minutes')} error={problem(errors.durationMinutes)}>
          <Input dir="ltr" inputMode="numeric" autoComplete="off" {...p.form.register('durationMinutes')} />
        </Field>
      </form>
      <p className="text-sm text-muted-foreground">{t('billing.services.taxNote')}</p>

      {p.service && (
        <section aria-labelledby="prices-heading" className="flex flex-col gap-3 border-t pt-3">
          <h3 id="prices-heading" className="text-base font-bold">{t('billing.prices.title')}</h3>
          {p.pricesLoading ? <p role="status">{t('billing.services.loading')}</p> : p.pricesFailed ? <p role="alert" className={fieldError}>{t('billing.services.loadFailed')}</p> : (
            <Table label={t('billing.prices.tableLabel')} stacked>
              <TableHeader>
                <TableRow>
                  <TableHead scope="col">{t('billing.prices.col.who')}</TableHead>
                  <TableHead scope="col">{t('billing.prices.col.now')}</TableHead>
                  <TableHead scope="col">{t('billing.prices.col.next')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {p.rows.map(r => (
                  <TableRow key={r.doctorId ?? 'clinic'}>
                    <TableHead scope="row">{rowName(r)}</TableHead>
                    <TableCell label={t('billing.prices.col.now')}>
                      {r.current ? money(r.current.amountMinor, r.current.currency) : r.doctorId ? t('billing.prices.usesDefault') : t('billing.services.noPrice')}
                    </TableCell>
                    <TableCell label={t('billing.prices.col.next')}>
                      {r.upcoming ? t('billing.prices.from', { amount: money(r.upcoming.amountMinor, r.upcoming.currency), date: formatDate(new Date(r.upcoming.effectiveFrom)) }) : t('billing.services.none')}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
          <form noValidate onSubmit={p.onAddPrice} className={fieldGrid}>
            <Select value={p.priceDoctor ?? 'clinic'} onValueChange={v => p.onPriceDoctor(v === 'clinic' ? null : v)}>
              <Field id="price-who" label={t('billing.prices.field.who')}>
                <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
              </Field>
              <SelectContent>{p.rows.map(r => <SelectItem key={r.doctorId ?? 'clinic'} value={r.doctorId ?? 'clinic'}>{rowName(r)}</SelectItem>)}</SelectContent>
            </Select>
            <Field id="price-amount" label={t('billing.prices.field.amount', { currency })} error={problem(p.priceForm.formState.errors.amount)}>
              <Input dir="ltr" inputMode="decimal" autoComplete="off" {...p.priceForm.register('amount')} />
            </Field>
            <Field id="price-from" label={t('billing.prices.field.from')} error={problem(p.priceForm.formState.errors.effectiveOn)}>
              <Input type="date" dir="ltr" min={p.today} {...p.priceForm.register('effectiveOn')} />
            </Field>
            <p className="col-span-full text-sm text-muted-foreground">{t('billing.prices.note')}</p>
            <div className="col-span-full flex flex-wrap items-center gap-3">
              <Button type="submit" variant="outline" loading={p.pricing}>{t('billing.prices.save')}</Button>
              {p.priceError && <p role="alert" className={fieldError}>{t(`billing.error.${p.priceError}`)}</p>}
            </div>
          </form>
          {preview && (
            <div className="rounded-md border p-3 text-sm">
              <p className="font-bold">{t('billing.preview.title')}</p>
              <p>{t('billing.preview.line', { description: preview.description, sac: preview.sac ?? t('billing.services.none'), amount: money(preview.unitMinor, currency) })}</p>
              <p>{t('billing.preview.tax', { tax: tax({ taxTreatment: values.taxTreatment, gstRateBasisPoints: taxable ? Math.round(Number(values.gstRate) * 100) : null }), amount: money(preview.taxMinor, currency) })}</p>
              <p className="font-bold">{t('billing.preview.total', { amount: money(preview.totalMinor, currency) })}</p>
            </div>
          )}
        </section>
      )}

      <StickyActionBar className={`${cardBar} mt-auto`} status={<>
        {p.formError && <p role="alert" className={fieldError}>{t(`billing.error.${p.formError}`)}</p>}
        {p.saved && <p role="status" className="flex items-center gap-2"><CircleCheck aria-hidden className="size-5" />{t('billing.services.saved')}</p>}
      </>}>
        {/* A consultation service holds every doctor's fee: renamed, never archived (the server refuses it too). */}
        {p.service && !p.service.consultationKind && <Button variant="outline" loading={p.archiving} onClick={p.onArchive}>{t(p.service.archivedAt ? 'billing.services.unarchive' : 'billing.services.archive')}</Button>}
        <Button type="submit" form="service-form" loading={p.saving}>{t('billing.services.save')}</Button>
      </StickyActionBar>
    </SheetContent>
  );
}
