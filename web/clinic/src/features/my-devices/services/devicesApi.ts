import { csrf, send } from '../../schedule/services/scheduleApi';
import type { DeviceSession } from '../domain/devices';

export const fetchDevices = () => send<DeviceSession[]>('/v1/me/sessions');
/** Ends one of the caller's own sessions; it stops on that device's next request. The id is the only thing in the URL. */
export const endDevice = (id: string) => send<void>(`/v1/me/sessions/${id}`, { method: 'DELETE', headers: csrf });
