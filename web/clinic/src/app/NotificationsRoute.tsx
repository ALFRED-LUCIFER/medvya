import NotificationCentre from '../features/notifications/components/NotificationCentre';
import { useNotificationsController } from '../features/notifications/hooks/useNotificationsController';

/** Composition only: the notification centre controller for the active facility, fed to the bell. */
export default function NotificationsRoute({ facilityId, online }: { facilityId: string; online: boolean }) {
  return <NotificationCentre {...useNotificationsController(facilityId, online)} />;
}
