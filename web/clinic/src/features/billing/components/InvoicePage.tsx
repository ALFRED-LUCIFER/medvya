import type { FormEventHandler } from 'react';
import { Link } from 'react-router';
import { Controller, type FieldError, type UseFormReturn } from 'react-hook-form';
import { ArrowLeft, CircleAlert, Hourglass, Link2, Printer, Sparkles } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import {
  Banner, Button, buttonVariants, Card, cn, Dialog, DialogContent, DialogDescription, DialogTitle, Field, Input, PageContainer, PageHeader, PageMessage,
  PatientBanner, SegmentedControl, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, StickyActionBar, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, Tag,
} from '@medvya/ui';
import { ageOn } from '../../registration/domain/registration';
import { todayIn, type Service } from '../domain/catalogue';
import {
  deskMethods, maxDiscountMinor, printPapers, type actionsFor, type coverage, type DiscountForm, type Invoice, type InvoiceProblem, type Payment, type PaymentForm,
  type PrintPaper,
} from '../domain/invoice';
import InvoiceStateChip from './InvoiceStateChip';

type Props = {
  timeZone: string; listPath: string;
  invoice: Invoice | null; status: 'loading' | 'missing' | 'forbidden' | 'error' | 'ready';
  actions: ReturnType<typeof actionsFor> | null; provider: Payment | null; coverage: ReturnType<typeof coverage> | null;
  problem: InvoiceProblem | null; busy: boolean;
  services: Service[]; serviceId: string; onServiceId: (id: string) => void; onAddLine: () => void;
  onQuantity: (lineId: string, quantity: number) => void; onRemoveLine: (lineId: string) => void;
  discountForm: UseFormReturn<DiscountForm>; onDiscount: FormEventHandler<HTMLFormElement>; discounting: boolean;
  issuing: boolean; onIssue: () => void;
  cancelling: boolean; cancelReason: string; onCancelReason: (v: string) => void; onStartCancel: () => void; onStopCancel: () => void; cancelPending: boolean; onCancel: () => void;
  paymentForm: UseFormReturn<PaymentForm>; method: PaymentForm['method']; paying: boolean; onPay: FormEventHandler<HTMLFormElement>; onFillBalance: () => void; onRecordCash: () => void;
  linkUrl: string | null; linking: boolean; onCreateLink: () => void; checking: boolean; onCheck: (paymentId: string) => void; onCancelLink: (paymentId: string) => void;
  paper: PrintPaper; onPaper: (paper: PrintPaper) => void; printing: 'bill' | 'receipt' | null; onPrint: (kind: 'bill' | 'receipt') => void;
};

/** One bill (P-06, MDV-P4-004): who it is for (identity only), its lines, discount and totals with the round-off line, and its payments. */
export default function InvoicePage(p: Props) {
  const { t, formatMoney, formatCalendarDate } = useI18n();
  const back = <Link to={p.listPath} className={cn(buttonVariants({ variant: 'ghost' }), 'px-2')}><ArrowLeft className="rtl:rotate-180" aria-hidden />{t('billing.invoice.back')}</Link>;
  const title = t('billing.invoices.title');
  if (p.status !== 'ready' || !p.invoice || !p.actions)
    return (
      <PageContainer>
        <PageHeader title={title} back={back} />
        {p.status === 'loading' ? <p role="status">{t('billing.invoices.loading')}</p>
          : <PageMessage kind={p.status === 'forbidden' ? 'denied' : 'error'} title={t(p.status === 'forbidden' ? 'err.403' : 'billing.invoices.title')}>
              {t(p.status === 'forbidden' ? 'billing.invoices.forbidden' : p.status === 'missing' ? 'billing.invoice.missing' : 'billing.invoices.loadFailed')}
            </PageMessage>}
      </PageContainer>
    );

  const i = p.invoice, a = p.actions;
  const money = (minor: number) => formatMoney(minor, i.currency);
  const age = ageOn(i.patient.dateOfBirth, todayIn(p.timeZone)) ?? 0;
  const tax = (l: Invoice['lines'][number]) => (l.taxTreatment === 'Exempt' ? t('billing.tax.Exempt') : t('billing.tax.rate', { rate: (l.gstRateBasisPoints ?? 0) / 100 }));
  const fieldProblem = (e: FieldError | undefined) => (e ? t(`billing.error.${e.message && e.message !== '' ? e.message : 'invalid'}` as MessageKey) : null);
  const methods = deskMethods.filter(m => i.options.paymentMethods.includes(m));
  const canLink = i.options.paymentMethods.includes('RazorpayLink');
  const cap = maxDiscountMinor(i.subtotalMinor, i.options.maxDiscountPercent);

  return (
    <PageContainer className="gap-5">
      <PageHeader back={back} title={i.number ? t('billing.invoice.title', { number: i.number }) : t('billing.invoice.draftTitle')} meta={<InvoiceStateChip state={i.state} />} />
      <PatientBanner variant="desk" patient={{ name: i.patient.fullName, age, sex: i.patient.sexAtBirth, mrn: i.patient.mrn, dob: formatCalendarDate(i.patient.dateOfBirth) }} />
      {i.state === 'Cancelled' && <Banner tone="neutral" title={t('billing.invoice.cancelled', { reason: i.cancelReason ?? '' })} />}
      <div aria-live="polite">{p.problem && <Banner tone="danger" icon={CircleAlert} title={t(`billing.error.${p.problem}`)} />}</div>

      <Card title={t('billing.invoice.lines')}>
        <Table label={t('billing.invoice.linesLabel')} stacked>
          <TableHeader>
            <TableRow>
              <TableHead scope="col">{t('billing.invoice.col.service')}</TableHead>
              <TableHead scope="col">{t('billing.invoice.col.quantity')}</TableHead>
              <TableHead scope="col">{t('billing.invoice.col.unit')}</TableHead>
              <TableHead scope="col">{t('billing.invoice.col.tax')}</TableHead>
              <TableHead scope="col">{t('billing.invoice.col.amount')}</TableHead>
              {a.edit && <TableHead scope="col">{t('billing.invoice.col.actions')}</TableHead>}
            </TableRow>
          </TableHeader>
          <TableBody>
            {i.lines.map(l => (
              <TableRow key={l.id}>
                <TableHead scope="row">
                  {l.description}
                  {l.sac && <span className="block text-sm text-muted-foreground">{t('billing.invoice.sac', { sac: l.sac })}</span>}
                  {l.suggested && <Tag tone="info" icon={Sparkles} size="sm" label={t('billing.invoice.suggested')} />}
                </TableHead>
                <TableCell label={t('billing.invoice.col.quantity')}>
                  {a.edit ? (
                    <Input type="number" inputMode="numeric" min={1} max={99} dir="ltr" className="w-24" defaultValue={l.quantity} key={`${l.id}-${l.quantity}`}
                      aria-label={t('billing.invoice.quantityFor', { service: l.description })} disabled={p.busy}
                      onBlur={e => { const q = Number(e.target.value); if (q !== l.quantity && q >= 1 && q <= 99) p.onQuantity(l.id, q); }} />
                  ) : l.quantity}
                </TableCell>
                <TableCell label={t('billing.invoice.col.unit')}>{money(l.unitMinor)}</TableCell>
                <TableCell label={t('billing.invoice.col.tax')}>{tax(l)}{l.taxMinor > 0 && <span className="block">{money(l.taxMinor)}</span>}</TableCell>
                <TableCell label={t('billing.invoice.col.amount')}>{money(l.amountMinor)}</TableCell>
                {a.edit && (
                  <TableCell label={t('billing.invoice.col.actions')}>
                    <span className="flex flex-wrap gap-2">
                      {l.suggested && <Button variant="outline" disabled={p.busy} onClick={() => p.onQuantity(l.id, l.quantity)}>{t('billing.invoice.confirm')}</Button>}
                      <Button variant="ghost" disabled={p.busy} onClick={() => p.onRemoveLine(l.id)} aria-label={t('billing.invoice.removeFor', { service: l.description })}>{t('billing.invoice.remove')}</Button>
                    </span>
                  </TableCell>
                )}
              </TableRow>
            ))}
          </TableBody>
        </Table>
        {i.lines.length === 0 && <p className="text-muted-foreground">{t('billing.invoice.noLines')}</p>}
        {a.edit && (
          <div className="flex flex-wrap items-end gap-3">
            <Select value={p.serviceId} onValueChange={p.onServiceId}>
              <Field id="invoice-service" label={t('billing.invoice.addService')} className="min-w-64">
                <SelectTrigger className="w-full"><SelectValue placeholder={t('billing.invoice.chooseService')} /></SelectTrigger>
              </Field>
              <SelectContent>
                {p.services.map(s => <SelectItem key={s.id} value={s.id}>{t('billing.invoice.serviceOption', { name: s.name, price: formatMoney(s.clinicPriceMinor ?? 0, s.currency) })}</SelectItem>)}
              </SelectContent>
            </Select>
            <Button variant="outline" disabled={!p.serviceId || p.busy} onClick={p.onAddLine}>{t('billing.invoice.addLine')}</Button>
          </div>
        )}
      </Card>

      <Card title={t('billing.invoice.totals')}>
        {a.edit && (
          <form noValidate onSubmit={p.onDiscount} className="grid gap-3 wide:grid-cols-3 wide:items-end">
            <Field id="invoice-discount" label={t('billing.invoice.discount', { currency: i.currency })} error={fieldProblem(p.discountForm.formState.errors.amount)}
              hint={i.options.mayExceedMaxDiscount ? undefined : t('billing.invoice.discountCap', { amount: money(cap), percent: i.options.maxDiscountPercent })}>
              <Input dir="ltr" inputMode="decimal" autoComplete="off" {...p.discountForm.register('amount')} />
            </Field>
            <Field id="invoice-discount-reason" label={t('billing.invoice.discountReason')} error={fieldProblem(p.discountForm.formState.errors.reason)} hint={t('billing.invoice.discountReasonHint')}>
              <Input list="discount-reasons" autoComplete="off" {...p.discountForm.register('reason')} />
            </Field>
            <datalist id="discount-reasons">{i.options.discountReasons.map(r => <option key={r} value={r} />)}</datalist>
            <Button type="submit" variant="outline" loading={p.discounting}>{t('billing.invoice.applyDiscount')}</Button>
          </form>
        )}
        <dl className="grid grid-cols-[1fr_auto] gap-x-6 gap-y-1 tabular-nums">
          <dt>{t('billing.invoice.subtotal')}</dt><dd className="text-end">{money(i.subtotalMinor)}</dd>
          {i.discountMinor > 0 && <><dt>{t('billing.invoice.discountLine', { reason: i.discountReason ?? '' })}</dt><dd className="text-end">{money(-i.discountMinor)}</dd></>}
          <dt>{t('billing.invoice.taxLine')}</dt><dd className="text-end">{money(i.taxMinor)}</dd>
          {/* Always shown, even 0.00 (the bill of supply design). */}
          <dt>{t('billing.invoice.roundOff')}</dt><dd className="text-end">{money(i.roundOffMinor)}</dd>
          <dt className="font-bold">{t('billing.invoice.total')}</dt><dd className="text-end font-bold">{money(i.totalMinor)}</dd>
          {i.state !== 'Draft' && i.state !== 'Cancelled' && <>
            <dt>{t('billing.invoice.paid')}</dt><dd className="text-end">{money(i.paidMinor)}</dd>
            <dt className="font-bold">{t('billing.invoice.balance')}</dt><dd className="text-end font-bold">{money(i.balanceMinor)}</dd>
          </>}
        </dl>
      </Card>

      {i.state !== 'Draft' && (
        <Card title={t('billing.invoice.payments')}>
          {i.payments.length === 0 ? <p className="text-muted-foreground">{t('billing.invoice.noPayments')}</p> : (
            <Table label={t('billing.invoice.paymentsLabel')} stacked>
              <TableHeader>
                <TableRow>
                  <TableHead scope="col">{t('billing.invoice.col.method')}</TableHead>
                  <TableHead scope="col">{t('billing.invoice.col.amount')}</TableHead>
                  <TableHead scope="col">{t('billing.invoice.col.state')}</TableHead>
                  <TableHead scope="col">{t('billing.invoice.col.reference')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {i.payments.map(pay => (
                  <TableRow key={pay.id}>
                    <TableHead scope="row">{t(`billing.method.${pay.method}`)}</TableHead>
                    <TableCell label={t('billing.invoice.col.amount')}>{money(pay.amountMinor)}</TableCell>
                    <TableCell label={t('billing.invoice.col.state')}>{t(`billing.payment.${pay.state === 'Pending' ? 'pending' : pay.state}`)}</TableCell>
                    <TableCell label={t('billing.invoice.col.reference')}>{pay.reference ? <bdi dir="ltr">{pay.reference}</bdi> : t('billing.services.none')}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}

          {p.provider?.state === 'Pending' && (
            <Banner tone="info" icon={Hourglass} title={t('billing.payment.pending')}
              action={<span className="flex flex-wrap gap-2">
                <Button variant="outline" loading={p.checking} disabled={p.busy && !p.checking} onClick={() => p.onCheck(p.provider!.id)}>{t('billing.payment.check')}</Button>
                <Button variant="ghost" disabled={p.busy} onClick={() => p.onCancelLink(p.provider!.id)}>{t('billing.payment.cancelLink')}</Button>
              </span>}>
              <p>{t('billing.payment.pendingBody', { amount: money(p.provider.amountMinor) })}</p>
              {p.linkUrl && <p className="flex flex-wrap items-center gap-2"><Link2 aria-hidden className="size-4" /><bdi dir="ltr" className="break-all font-mono">{p.linkUrl}</bdi></p>}
            </Banner>
          )}
          {p.provider?.state === 'Failed' && a.pay && (
            <Banner tone="danger" icon={CircleAlert} title={t('billing.payment.Failed')}
              action={<span className="flex flex-wrap gap-2">
                {canLink && <Button variant="outline" loading={p.linking} onClick={p.onCreateLink}>{t('billing.payment.retry')}</Button>}
                {methods.includes('Cash') && <Button variant="ghost" onClick={p.onRecordCash}>{t('billing.payment.recordCash')}</Button>}
              </span>}>
              <p>{t('billing.payment.failedBody')}</p>
            </Banner>
          )}

          {a.pay && (
            <form noValidate onSubmit={p.onPay} className="flex flex-col gap-3">
              <h3 className="text-label">{t('billing.payment.record')}</h3>
              <div className="grid gap-3 wide:grid-cols-3 wide:items-end">
                <Controller control={p.paymentForm.control} name="method" render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <Field id="payment-method" label={t('billing.payment.method')}>
                      <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
                    </Field>
                    <SelectContent>{methods.map(m => <SelectItem key={m} value={m}>{t(`billing.method.${m}`)}</SelectItem>)}</SelectContent>
                  </Select>
                )} />
                <Field id="payment-amount" label={t('billing.payment.amount', { currency: i.currency })} error={fieldProblem(p.paymentForm.formState.errors.amount)}>
                  <Input dir="ltr" inputMode="decimal" autoComplete="off" {...p.paymentForm.register('amount')} />
                </Field>
                {p.method !== 'Cash' && (
                  <Field id="payment-reference" label={t(p.method === 'Card' ? 'billing.payment.cardReference' : 'billing.payment.utr')}
                    hint={t('billing.payment.referenceHint')} error={fieldProblem(p.paymentForm.formState.errors.reference)}>
                    <Input dir="ltr" autoComplete="off" {...p.paymentForm.register('reference')} />
                  </Field>
                )}
              </div>
              {p.coverage && (
                <p role="status" className={cn('text-label', p.coverage.kind === 'over' && 'text-danger-700')}>
                  {p.coverage.kind === 'over' && <CircleAlert aria-hidden className="me-1 inline size-4" />}
                  {t(`billing.payment.coverage.${p.coverage.kind}`, { amount: money(p.coverage.minor) })}
                </p>
              )}
              <div className="flex flex-wrap gap-2">
                <Button type="submit" variant="outline" loading={p.paying} disabled={p.coverage?.kind === 'over'}>{t('billing.payment.recordAction')}</Button>
                <Button type="button" variant="ghost" onClick={p.onFillBalance}>{t('billing.payment.fillBalance')}</Button>
              </div>
            </form>
          )}
        </Card>
      )}

      {(a.issue || a.edit || a.cancel || (a.pay && canLink) || a.printBill) && (
        <StickyActionBar status={a.printBill && (
          <SegmentedControl label={t('billing.print.paper')} value={p.paper} onValueChange={p.onPaper}
            options={printPapers.map(v => ({ value: v, label: t(`billing.print.paper.${v}`) }))} />
        )}>
          {a.printBill && (
            <Button variant="outline" loading={p.printing === 'bill'} disabled={p.printing !== null && p.printing !== 'bill'} onClick={() => p.onPrint('bill')}>
              <Printer aria-hidden />{t('billing.print.billPdf')}
            </Button>
          )}
          {a.printReceipt && (
            <Button variant="outline" loading={p.printing === 'receipt'} disabled={p.printing !== null && p.printing !== 'receipt'} onClick={() => p.onPrint('receipt')}>
              <Printer aria-hidden />{t('billing.print.receiptPdf')}
            </Button>
          )}
          {a.cancel && <Button variant="ghost" disabled={p.busy} onClick={p.onStartCancel}>{t('billing.invoice.cancel')}</Button>}
          {a.pay && canLink && <Button variant="outline" loading={p.linking} disabled={p.busy && !p.linking} onClick={p.onCreateLink}>{t('billing.payment.sendLink')}</Button>}
          {a.edit && <Button loading={p.issuing} disabled={!a.issue || (p.busy && !p.issuing)} onClick={p.onIssue}>{t('billing.invoice.issue')}</Button>}
        </StickyActionBar>
      )}
      {a.edit && !a.issue && i.lines.some(l => l.suggested) && <p className="text-sm text-muted-foreground">{t('billing.invoice.confirmFirst')}</p>}

      <Dialog open={p.cancelling} onOpenChange={o => { if (!o) p.onStopCancel(); }}>
        <DialogContent closeLabel={t('billing.invoice.cancelClose')}>
          <DialogTitle>{t('billing.invoice.cancelTitle')}</DialogTitle>
          <DialogDescription>{t('billing.invoice.cancelBody')}</DialogDescription>
          <Field id="cancel-reason" label={t('billing.invoice.cancelReason')}>
            <Input autoComplete="off" value={p.cancelReason} onChange={e => p.onCancelReason(e.target.value)} />
          </Field>
          <div className="flex flex-wrap gap-2">
            <Button variant="destructive" loading={p.cancelPending} disabled={!p.cancelReason.trim()} onClick={p.onCancel}>{t('billing.invoice.cancelConfirm')}</Button>
            <Button variant="outline" disabled={p.cancelPending} onClick={p.onStopCancel}>{t('billing.invoice.cancelKeep')}</Button>
          </div>
        </DialogContent>
      </Dialog>
    </PageContainer>
  );
}
