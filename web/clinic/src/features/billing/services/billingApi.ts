import { post, send } from '../../api/services/apiClient';
import type { BillingSettings } from '../domain/billingSettings';
import type { PriceVersion, Service } from '../domain/catalogue';

// Settings, Services and Settings, Billing (MDV-P4-002). Ids in paths, details in bodies; no patient data travels.
const put = <T>(url: string, body: unknown) => send<T>(url, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });

export const fetchServices = (archived: boolean) => send<Service[]>(`/v1/settings/services?${new URLSearchParams({ archived: String(archived) })}`);
export const createService = (body: unknown) => post<Service>('/v1/settings/services', body);
export const updateService = (id: string, body: unknown) => put<Service>(`/v1/settings/services/${id}`, body);
export const archiveService = (id: string, archived: boolean) => post<Service>(`/v1/settings/services/${id}/${archived ? 'archive' : 'unarchive'}`, {});
export const fetchPrices = (id: string) => send<PriceVersion[]>(`/v1/settings/services/${id}/prices`);
export const addPrice = (id: string, body: unknown) => post<PriceVersion>(`/v1/settings/services/${id}/prices`, body);
export const fetchBillingSettings = () => send<BillingSettings>('/v1/settings/billing');
export const saveBillingSettings = (body: unknown) => put<BillingSettings>('/v1/settings/billing', body);
