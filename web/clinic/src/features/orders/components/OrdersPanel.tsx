import type { FormEventHandler } from 'react';
import { Link } from 'react-router';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { Check, CircleCheck, Minus, Pencil } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Switch, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableState } from '@medvya/ui';
import { describedBy, fieldError } from '../../settings/components/fieldStyles';
import { Field } from '../../settings/components/Field';
import { Section } from '../../settings/components/Section';
import { canSwitch, hasDrafts, hasPlaced, isIncluded, orderColumns, orderKinds, type Order, type OrderForm, type OrderProblem, type OrderSetChoice } from '../domain/orders';

type Props = {
  encounterId: string;
  ended: boolean;
  orders: Order[];
  loading: boolean;
  failed: boolean;
  sets: OrderSetChoice[];
  setId: string; onSetId: (id: string) => void;
  form: UseFormReturn<OrderForm>;
  destination: string; onDestination: (value: string) => void;
  problem: OrderProblem | null;
  placed: boolean;
  busy: boolean;
  onAdd: FormEventHandler<HTMLFormElement>;
  onAddSet: () => void;
  onInclude: (id: string, included: boolean) => void;
  onPlace: () => void;
};

const icons = { Draft: Pencil, Ordered: Check, NotIncluded: Minus } as const;

/** Orders (E-05): the visit's tests as drafts, switched on or off, placed on purpose with "Place orders". Placing is never done by anything else. */
export default function OrdersPanel(p: Props) {
  const { t } = useI18n();
  const { errors } = p.form.formState;
  const tableState = p.loading ? 'loading' : p.failed ? 'error' : p.orders.length === 0 ? 'empty' : 'ready';
  const nameError = errors.name && t('orders.error.required');

  return (
    <Section title={t('orders.title')} intro={t(p.ended ? 'orders.ended' : 'orders.intro')}>
      <TableState state={tableState} messages={{ loading: t('orders.loading'), error: t('orders.loadFailed'), empty: t('orders.empty') }}>
        <Table label={t('orders.tableLabel')} className="min-w-[640px]">
          <TableHeader>
            <TableRow>{orderColumns.map(c => <TableHead key={c} scope="col">{t(`orders.col.${c}`)}</TableHead>)}</TableRow>
          </TableHeader>
          <TableBody>
            {p.orders.map(o => {
              const Icon = icons[o.state];
              return (
                <TableRow key={o.id}>
                  <TableHead scope="row">{o.name}</TableHead>
                  <TableCell>{t(`orders.kind.${o.kind}`)}</TableCell>
                  <TableCell>{o.note}</TableCell>
                  <TableCell><span className="inline-flex items-center gap-1.5 font-bold"><Icon aria-hidden className="size-4 shrink-0" />{t(`orders.state.${o.state}`)}</span></TableCell>
                  <TableCell>
                    {canSwitch(o) && !p.ended && <Switch checked={isIncluded(o)} disabled={p.busy} aria-label={t('orders.include', { name: o.name })} onCheckedChange={on => p.onInclude(o.id, on)} />}
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </TableState>

      {!p.ended && (
        <>
          <form noValidate onSubmit={p.onAdd} className="grid gap-4 sm:grid-cols-2">
            <h3 className="font-bold sm:col-span-2">{t('orders.add.title')}</h3>
            <Field id="order-kind" label={t('orders.field.kind')}>
              <Controller control={p.form.control} name="kind" render={({ field }) => (
                <Select value={field.value ?? 'Lab'} onValueChange={field.onChange}>
                  <SelectTrigger id="order-kind" className="w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>{orderKinds.map(k => <SelectItem key={k} value={k}>{t(`orders.kind.${k}`)}</SelectItem>)}</SelectContent>
                </Select>
              )} />
            </Field>
            <Field id="order-name" label={t('orders.field.name')} error={nameError}>
              <Input id="order-name" autoComplete="off" {...describedBy('order-name', !!errors.name)} {...p.form.register('name')} />
            </Field>
            <Field id="order-note" label={t('orders.field.note')} className="sm:col-span-2">
              <Input id="order-note" autoComplete="off" {...p.form.register('note')} />
            </Field>
            <Button type="submit" variant="outline" className="self-start" disabled={p.busy}>{t('orders.add')}</Button>
          </form>

          {p.sets.length > 0 && (
            <div className="flex flex-wrap items-end gap-3">
              <Field id="order-set" label={t('orders.set.field')} className="min-w-64">
                <Select value={p.setId} onValueChange={p.onSetId}>
                  <SelectTrigger id="order-set" className="w-full"><SelectValue placeholder={t('orders.set.choose')} /></SelectTrigger>
                  <SelectContent>{p.sets.map(s => <SelectItem key={s.id} value={s.id}>{s.name}</SelectItem>)}</SelectContent>
                </Select>
              </Field>
              <Button type="button" variant="outline" disabled={p.busy || !p.setId} onClick={p.onAddSet}>{t('orders.set.add')}</Button>
            </div>
          )}

          <div className="flex flex-wrap items-end gap-3 border-t pt-4">
            <Field id="order-destination" label={t('orders.destination')} className="min-w-64">
              <Input id="order-destination" maxLength={100} autoComplete="off" value={p.destination} onChange={e => p.onDestination(e.target.value)} />
            </Field>
            <Button type="button" disabled={p.busy || !hasDrafts(p.orders)} onClick={p.onPlace}>{t('orders.place')}</Button>
          </div>
        </>
      )}

      {p.problem && <p role="alert" className={fieldError}>{t(`orders.error.${p.problem}`)}</p>}
      {p.placed && <p role="status" className="flex items-center gap-2 font-bold"><CircleCheck aria-hidden className="size-5" />{t('orders.placed')}</p>}
      {hasPlaced(p.orders) && <Link to={`/consult/${p.encounterId}/slip`} className={buttonVariants({ variant: 'outline', className: 'self-start' })}>{t('orders.slipLink')}</Link>}
    </Section>
  );
}
