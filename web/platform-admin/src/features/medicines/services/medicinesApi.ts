import { getJson, post } from '../../api/services/api';
import { searchLimit, toBody, type Country, type Medicine, type MedicineForm, type RegulatoryClass } from '../domain/medicines';

// The query carries a medicine name and a country, never a patient value.
export const searchMedicines = (country: Country, q: string) =>
  getJson<Medicine[]>(`/v1/medicines?${new URLSearchParams({ country, q, limit: String(searchLimit) })}`);

export const addMedicine = async (country: Country, form: MedicineForm) =>
  (await post('/v1/medicines/', { country, ...toBody(form) })).json() as Promise<Medicine>;

// The class has its own action, so a correction leaves it out (undefined is not sent).
export const correctMedicine = async (id: string, form: MedicineForm) =>
  (await post(`/v1/medicines/${encodeURIComponent(id)}/correction`, { ...toBody(form), regClass: undefined })).json() as Promise<Medicine>;

export const setClass = async (id: string, regClass: RegulatoryClass, reason: string) =>
  (await post(`/v1/medicines/${encodeURIComponent(id)}/class`, { regClass, reason })).json() as Promise<Medicine>;

export const deactivate = async (id: string, reason: string) =>
  (await post(`/v1/medicines/${encodeURIComponent(id)}/deactivation`, { reason })).json() as Promise<Medicine>;
