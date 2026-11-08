import { post, send } from '../../schedule/services/scheduleApi';
import { windowDays, type Baseline, type DeliveryReport, type PilotMetrics } from '../domain/pilotMetrics';

const query = (facilityId: string) => new URLSearchParams({ facilityId, days: String(windowDays) });

export const fetchPilotMetrics = (facilityId: string) => send<PilotMetrics>(`/v1/schedule/metrics?${query(facilityId)}`);

export const fetchDelivery = (facilityId: string) => send<DeliveryReport>(`/v1/notifications/delivery?${query(facilityId)}`);

/** The method text goes in the body only. */
export const recordBaseline = (body: unknown) => post<Baseline>('/v1/schedule/metrics/baselines', body);
