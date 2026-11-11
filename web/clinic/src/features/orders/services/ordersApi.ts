import { csrf, post, send } from '../../schedule/services/scheduleApi';
import { downloadPaperPdf } from '../../prescription/services/prescriptionApi';
import { toOrderBody, type Order, type OrderForm, type OrderSetChoice, type OrderSlip } from '../domain/orders';

// Investigation orders (MDV-P2-018). Ids in paths; test names and instructions in bodies, never in a URL.
export const fetchOrders = (encounterId: string) => send<Order[]>(`/v1/encounters/${encounterId}/orders`);
export const addOrder = (encounterId: string, form: OrderForm) => post<Order[]>(`/v1/encounters/${encounterId}/orders`, toOrderBody(form));
export const addOrderSet = (encounterId: string, orderSetId: string) => post<Order[]>(`/v1/encounters/${encounterId}/orders/from-set`, { orderSetId });
export const includeOrder = (orderId: string, included: boolean) =>
  send<Order[]>(`/v1/orders/${orderId}/included`, { method: 'PUT', headers: { ...csrf, 'Content-Type': 'application/json' }, body: JSON.stringify({ included }) });
export const placeOrders = (encounterId: string, destination: string) => post<Order[]>(`/v1/encounters/${encounterId}/orders/place`, { destination: destination.trim() || null });
export const fetchOrderSets = () => send<OrderSetChoice[]>('/v1/order-sets');
export const fetchSlip = (encounterId: string) => send<OrderSlip>(`/v1/encounters/${encounterId}/orders/slip`);
export const downloadSlipPdf = (encounterId: string) => downloadPaperPdf(`/v1/encounters/${encounterId}/orders/slip/pdf`, 'investigation-slip.pdf');

/** The browser's print dialog, where "Save as PDF" makes the slip's PDF. */
export const printPage = () => window.print();
