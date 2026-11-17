import { post, send } from '../../api/services/apiClient';
import type { Service } from '../domain/catalogue';
import type { Invoice, InvoiceRow, InvoiceState, PaymentLinkResult, PrintPaper } from '../domain/invoice';
import { downloadPaperPdf } from '../../prescription/services/prescriptionApi';

// Bills and payments (MDV-P4-004). Ids in paths; filters, amounts and reasons in bodies, so no patient detail reaches a URL.
const put = <T>(url: string, body: unknown) => send<T>(url, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
const bill = (id: string) => `/v1/billing/invoices/${id}`;

export const searchInvoices = (filters: { state: InvoiceState | null; from: string | null; to: string | null; patientId: string | null }) =>
  post<InvoiceRow[]>('/v1/billing/invoices/search', filters);
export const fetchInvoice = (id: string) => send<Invoice>(bill(id));
export const createInvoice = (patientId: string) => post<Invoice>('/v1/billing/invoices', { patientId });
export const openVisitInvoice = (appointmentId: string) => post<Invoice>(`/v1/billing/visits/${appointmentId}/invoice`, {});
export const addLine = (id: string, body: { serviceId: string; quantity: number; version: number }) => post<Invoice>(`${bill(id)}/lines`, body);
export const updateLine = (id: string, lineId: string, body: { quantity: number; version: number }) => put<Invoice>(`${bill(id)}/lines/${lineId}`, body);
export const removeLine = (id: string, lineId: string, version: number) => post<Invoice>(`${bill(id)}/lines/${lineId}/remove`, { version });
export const setDiscount = (id: string, body: { amountMinor: number; reason: string | null; version: number }) => put<Invoice>(`${bill(id)}/discount`, body);
export const issueInvoice = (id: string, version: number) => post<Invoice>(`${bill(id)}/issue`, { version });
export const cancelInvoice = (id: string, reason: string, version: number) => post<Invoice>(`${bill(id)}/cancel`, { reason, version });
export const recordPayment = (id: string, body: { method: string; amountMinor: number; reference: string | null }) => post<Invoice>(`${bill(id)}/payments`, body);
export const createPaymentLink = (id: string) => post<PaymentLinkResult>(`${bill(id)}/payment-link`, {});
export const checkPayment = (id: string, paymentId: string) => post<Invoice>(`${bill(id)}/payments/${paymentId}/check`, {});
export const cancelPaymentLink = (id: string, paymentId: string) => post<Invoice>(`${bill(id)}/payments/${paymentId}/cancel`, {});
/** The bill or its receipt as a PDF (MDV-P4-005): the first print issues and stores it, later ones return the same paper. Saved under a name with no patient data. */
export const downloadBillingPdf = (id: string, kind: 'bill' | 'receipt', paper: PrintPaper) =>
  downloadPaperPdf(`${bill(id)}/documents/${kind}/${paper}`, `${kind}-${paper}.pdf`);
/** The active catalogue with clinic prices, for the desk's "Add line" (Invoices and payments at Full). */
export const fetchBillableServices = () => send<Service[]>('/v1/billing/services');
