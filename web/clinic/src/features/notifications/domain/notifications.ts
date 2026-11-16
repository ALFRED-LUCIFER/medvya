// GET /v1/notifications/inbox (IStaffNotifications): a kind and a pointer, worded here. No patient names or message text ever arrive.
export const notificationKinds = ['MessageFailed', 'LicenceExpiring', 'AccessReviewDue', 'AccessRequested', 'RegistrationExpiring', 'VisitsNeedRebooking'] as const;
export type NotificationKind = (typeof notificationKinds)[number];
export type StaffNotification = {
  id: string; kind: NotificationKind;
  /** The visit, request or (visits to rebook) the doctor's calendar it points at. */
  refId: string | null;
  /** Days until a licence expires (0 or less: expired), or how many visits need rebooking; null for other kinds. */
  detail: number | null;
  /** Visits to rebook: the first facility-local day (YYYY-MM-DD) affected; null for other kinds. */
  day: string | null;
  createdAt: string; read: boolean;
};
export type NotificationList = { unread: number; items: StaffNotification[] };

/** The bell shows at most 9+, so a long backlog does not widen the header. */
export const badgeText = (unread: number) => (unread > 9 ? '9+' : String(unread));

/** Where each kind sends the reader (the board marks the visit whose message failed). */
const targets: Record<NotificationKind, string> = {
  MessageFailed: '/front-desk', LicenceExpiring: '/settings?tab=doctors', AccessReviewDue: '/settings?tab=review', AccessRequested: '/settings?tab=requests',
  RegistrationExpiring: '/settings?tab=profile', VisitsNeedRebooking: '/schedule',
};

/** Visits to rebook open the doctor's week at the first day affected, where each moved visit shows as "needs rebooking". */
export const notificationTarget = (n: StaffNotification) =>
  n.kind === 'VisitsNeedRebooking' && n.refId && n.day ? `/schedule?${new URLSearchParams({ date: n.day, view: 'week', doctor: n.refId })}` : targets[n.kind];
