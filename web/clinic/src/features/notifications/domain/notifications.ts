// GET /v1/notifications/inbox (IStaffNotifications): a kind and a pointer, worded here. No patient names or message text ever arrive.
export const notificationKinds = ['MessageFailed'] as const;
export type NotificationKind = (typeof notificationKinds)[number];
export type StaffNotification = { id: string; kind: NotificationKind; refId: string | null; createdAt: string; read: boolean };
export type NotificationList = { unread: number; items: StaffNotification[] };

/** The bell shows at most 9+, so a long backlog does not widen the header. */
export const badgeText = (unread: number) => (unread > 9 ? '9+' : String(unread));
