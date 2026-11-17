import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm, useWatch } from 'react-hook-form';
import { ApiError } from '../../schedule/services/scheduleApi';
import { toMinor } from '../domain/catalogue';
import {
  actionsFor, coverage, discountSchema, invoiceProblem, paymentSchema, providerPayment, toMajor, toPaymentBody,
  type DiscountForm, type Invoice, type InvoiceProblem, type PaymentForm,
} from '../domain/invoice';
import {
  addLine, cancelInvoice, cancelPaymentLink, checkPayment, createPaymentLink, fetchBillableServices, fetchInvoice, issueInvoice, recordPayment, removeLine,
  setDiscount, updateLine,
} from '../services/invoicesApi';

const problemOf = (e: unknown) => invoiceProblem(e instanceof ApiError ? e.code : undefined);

function useAct<A>(fn: (arg: A) => Promise<Invoice>, done: (bill: Invoice) => void, failed: (e: unknown) => void) {
  return useMutation({ mutationFn: fn, onSuccess: done, onError: failed });
}

/**
 * One bill (P-06, MDV-P4-004): lines from the catalogue, the discount with its reason, issue, cancel, and split payments (cash, card on terminal, UPI direct,
 * a Razorpay link). Every answer is the whole bill, put straight into the cache; a stale edit (409) reloads it. The server checks everything again.
 */
export function useInvoiceController(id: string) {
  const queryClient = useQueryClient();
  const key = ['billing', 'invoice', id];
  const query = useQuery({ queryKey: key, queryFn: () => fetchInvoice(id) });
  const invoice = query.data ?? null;
  const services = useQuery({ queryKey: ['billing', 'services', 'desk'], queryFn: fetchBillableServices, enabled: invoice?.state === 'Draft' });
  const [problem, setProblem] = useState<InvoiceProblem | null>(null);
  const [linkUrl, setLinkUrl] = useState<string | null>(null);
  const [cancelling, setCancelling] = useState(false);
  const [cancelReason, setCancelReason] = useState('');
  const [serviceId, setServiceId] = useState('');

  const done = (bill: Invoice) => { queryClient.setQueryData(key, bill); setProblem(null); void queryClient.invalidateQueries({ queryKey: ['billing', 'invoices'] }); };
  const failed = (e: unknown) => {
    const p = problemOf(e);
    setProblem(p);
    if (p === 'stale' || p === 'state' || p.startsWith('provider_')) void query.refetch(); // someone else changed it, or the attempt is now a failed payment
  };
  const b = () => invoice!; // every action is offered only once the bill is loaded
  const add = useAct((service: string) => addLine(id, { serviceId: service, quantity: 1, version: b().version }), done, failed);
  const quantity = useAct((a: { lineId: string; quantity: number }) => updateLine(id, a.lineId, { quantity: a.quantity, version: b().version }), done, failed);
  const remove = useAct((lineId: string) => removeLine(id, lineId, b().version), done, failed);
  const discount = useAct((f: DiscountForm) => setDiscount(id, { amountMinor: toMinor(f.amount), reason: f.reason || null, version: b().version }), done, failed);
  const issue = useAct(() => issueInvoice(id, b().version), done, failed);
  const cancel = useAct((reason: string) => cancelInvoice(id, reason, b().version), done, failed);
  const pay = useAct((f: PaymentForm) => recordPayment(id, toPaymentBody(f)), done, failed);
  const check = useAct((paymentId: string) => checkPayment(id, paymentId), done, failed);
  const closeLink = useAct((paymentId: string) => cancelPaymentLink(id, paymentId), done, failed);
  const link = useMutation({
    mutationFn: () => createPaymentLink(id),
    onSuccess: r => { done(r.invoice); setLinkUrl(r.shortUrl); },
    onError: failed,
  });

  const discountForm = useForm<DiscountForm>({ resolver: zodResolver(discountSchema), values: { amount: toMajor(invoice?.discountMinor ?? 0), reason: invoice?.discountReason ?? '' } });
  const paymentForm = useForm<PaymentForm>({ resolver: zodResolver(paymentSchema), defaultValues: { method: 'Cash', amount: '', reference: '' } });
  const entered = toMinor(useWatch({ control: paymentForm.control, name: 'amount' }) || '0');
  const method = useWatch({ control: paymentForm.control, name: 'method' });
  const busy = [add, quantity, remove, discount, issue, cancel, pay, check, closeLink, link].some(m => m.isPending);

  return {
    invoice, loading: query.isPending,
    status: query.isPending ? ('loading' as const) : query.error instanceof ApiError && query.error.status === 404 ? ('missing' as const)
      : query.error instanceof ApiError && query.error.status === 403 ? ('forbidden' as const) : query.isError ? ('error' as const) : ('ready' as const),
    actions: invoice ? actionsFor(invoice) : null,
    provider: invoice ? providerPayment(invoice.payments) : null,
    coverage: invoice ? coverage(invoice.balanceMinor, Number.isFinite(entered) ? entered : 0) : null,
    problem, busy,
    services: (services.data ?? []).filter(s => s.clinicPriceMinor !== null),
    serviceId, onServiceId: setServiceId,
    onAddLine: () => { if (serviceId) add.mutate(serviceId, { onSuccess: () => setServiceId('') }); },
    onQuantity: (lineId: string, value: number) => quantity.mutate({ lineId, quantity: value }),
    onRemoveLine: (lineId: string) => remove.mutate(lineId),
    discountForm, onDiscount: discountForm.handleSubmit(f => discount.mutate(f)), discounting: discount.isPending,
    issuing: issue.isPending, onIssue: () => issue.mutate(undefined),
    cancelling, cancelReason, onCancelReason: setCancelReason, onStartCancel: () => { setCancelReason(''); setCancelling(true); },
    onStopCancel: () => setCancelling(false), cancelPending: cancel.isPending,
    onCancel: () => { if (cancelReason.trim()) cancel.mutate(cancelReason.trim(), { onSuccess: () => setCancelling(false) }); },
    paymentForm, method, paying: pay.isPending,
    onPay: paymentForm.handleSubmit(f => pay.mutate(f, { onSuccess: () => paymentForm.reset({ method: f.method, amount: '', reference: '' }) })),
    onFillBalance: () => invoice && paymentForm.setValue('amount', toMajor(invoice.balanceMinor), { shouldValidate: true }),
    // "Payment failed · Record cash instead": the form set to cash for the balance, ready to confirm.
    onRecordCash: () => {
      if (!invoice) return;
      paymentForm.reset({ method: 'Cash', amount: toMajor(invoice.balanceMinor), reference: '' });
      paymentForm.setFocus('amount');
    },
    linkUrl: linkUrl ?? (invoice ? providerPayment(invoice.payments)?.linkUrl ?? null : null), linking: link.isPending, onCreateLink: () => link.mutate(),
    checking: check.isPending, onCheck: (paymentId: string) => check.mutate(paymentId),
    onCancelLink: (paymentId: string) => closeLink.mutate(paymentId),
  };
}
