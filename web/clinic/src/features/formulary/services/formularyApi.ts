import { post, send } from '../../schedule/services/scheduleApi';
import { toMedicineBody, toOrderSetBody, type FormularyItem, type MedicineForm, type OrderSet, type OrderSetForm } from '../domain/formulary';

// Settings, Medicines (F-13, MDV-P2-014). Only medicine names and test names travel, never a patient value.
export const searchFormulary = (query: string) => send<FormularyItem[]>(`/v1/settings/formulary?${new URLSearchParams({ q: query, limit: '50' })}`);
export const addMedicine = (form: MedicineForm) => post<FormularyItem>('/v1/settings/formulary', toMedicineBody(form));
export const fetchOrderSets = () => send<OrderSet[]>('/v1/settings/order-sets');
export const addOrderSet = (form: OrderSetForm) => post<OrderSet>('/v1/settings/order-sets', toOrderSetBody(form));
