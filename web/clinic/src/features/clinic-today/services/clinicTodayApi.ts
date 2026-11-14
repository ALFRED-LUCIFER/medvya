import { send } from '../../schedule/services/scheduleApi';
import type { ClinicToday } from '../domain/clinicToday';

/** The owner's Clinic today of the facility (MDV-P2-053). Ids only in the URL; the server checks practice configuration. */
export const fetchClinicToday = (facilityId: string) => send<ClinicToday>(`/v1/front-desk/clinic-today?${new URLSearchParams({ facilityId })}`);
