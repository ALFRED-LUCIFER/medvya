import { post, send } from '../../schedule/services/scheduleApi';
import type { NotificationList } from '../domain/notifications';

// Notification centre (D-03, MDV-P1-034). Ids only, in the URL or the body; the server decides what this role may see.
export const fetchInbox = (facilityId: string) => send<NotificationList>(`/v1/notifications/inbox?${new URLSearchParams({ facilityId })}`);
export const markAllRead = (facilityId: string) => post<void>('/v1/notifications/inbox/read-all', { facilityId });
export const markRead = (facilityId: string, id: string) => post<void>(`/v1/notifications/inbox/${id}/read`, { facilityId });
