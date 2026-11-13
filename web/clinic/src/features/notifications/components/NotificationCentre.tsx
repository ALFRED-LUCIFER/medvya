import { useEffect, useRef } from 'react';
import { Link } from 'react-router';
import { Bell, MessageSquareWarning } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import { badgeText, type NotificationKind, type NotificationList, type StaffNotification } from '../domain/notifications';

type Props = {
  open: boolean;
  list: NotificationList | null;
  unread: number;
  loading: boolean;
  failed: boolean;
  online: boolean;
  marking: boolean;
  onToggle: () => void;
  onClose: () => void;
  onRetry: () => void;
  onMarkAllRead: () => void;
  onMarkRead: (id: string) => void;
};

/** Where each kind sends the reader (the board marks the visit whose message failed). */
const targets: Record<NotificationKind, string> = { MessageFailed: '/front-desk', LicenceExpiring: '/settings?tab=doctors', AccessReviewDue: '/settings?tab=review', AccessRequested: '/settings?tab=requests', RegistrationExpiring: '/settings?tab=profile' };

/** The words of one item; a licence says how many days are left, or that it has expired. */
const words = (t: ReturnType<typeof useI18n>['t'], n: StaffNotification) =>
  n.kind === 'LicenceExpiring' ? ((n.detail ?? 0) <= 0 ? t('notifications.kind.LicenceExpired') : t('notifications.kind.LicenceExpiring', { days: n.detail! }))
    : n.kind === 'RegistrationExpiring' ? ((n.detail ?? 0) <= 0 ? t('notifications.kind.RegistrationExpired') : t('notifications.kind.RegistrationExpiring', { days: n.detail! }))
    : t(`notifications.kind.${n.kind}`);

/** D-03: the bell with its unread count and an overlay listing staff notifications, newest first, with "Mark all read". */
export default function NotificationCentre(props: Props) {
  const { t, formatTime, formatDate } = useI18n();
  const { open, list, unread } = props;
  const bell = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  useEffect(() => { if (open) panel.current?.focus(); }, [open]);
  const close = () => { props.onClose(); bell.current?.focus(); };
  return (
    <div className="relative" onKeyDown={e => { if (e.key === 'Escape' && open) close(); }}>
      <Button ref={bell} variant="ghost" size="icon" className="relative" aria-expanded={open} aria-haspopup="dialog" aria-controls="notification-panel"
        aria-label={unread > 0 ? t('notifications.openUnread', { count: unread }) : t('shell.notifications')} onClick={props.onToggle}>
        <Bell className="size-5" aria-hidden />
        {unread > 0 && <span aria-hidden className="absolute -end-0.5 -top-0.5 grid min-w-5 place-items-center rounded-full bg-danger-700 px-1 text-[11px] font-extrabold text-white">{badgeText(unread)}</span>}
      </Button>
      {open && (
        <div id="notification-panel" ref={panel} role="dialog" aria-label={t('shell.notifications')} tabIndex={-1}
          className="absolute end-0 top-full z-20 mt-2 flex max-h-[70vh] w-[min(22rem,calc(100vw-2rem))] flex-col gap-2 overflow-y-auto rounded-lg border bg-popover p-3 text-popover-foreground shadow-popover focus:outline-none">
          <div className="flex items-center justify-between gap-2">
            <h2 className="text-[15px] font-extrabold">{t('shell.notifications')}</h2>
            <Button variant="outline" disabled={unread === 0 || props.marking || !props.online} onClick={props.onMarkAllRead}>{t('notifications.markAllRead')}</Button>
          </div>
          {props.loading && <p role="status" className="text-sm text-muted-foreground">{t('notifications.loading')}</p>}
          {props.failed && !list && (
            <div role="alert" className="flex flex-col items-start gap-2">
              <p className="text-sm">{t('notifications.error')}</p>
              <Button variant="outline" onClick={props.onRetry}>{t('schedule.retry')}</Button>
            </div>
          )}
          {list && list.items.length === 0 && <p className="text-sm text-muted-foreground">{t('notifications.empty')}</p>}
          {list && list.items.length > 0 && (
            <ul className="flex flex-col gap-2">
              {list.items.map(n => (
                <li key={n.id} className="rounded-lg border">
                  <Link to={targets[n.kind]} className="flex min-h-11 items-start gap-2 p-2.5 text-sm" onClick={() => { if (!n.read) props.onMarkRead(n.id); close(); }}>
                    <MessageSquareWarning aria-hidden className="mt-0.5 size-4 shrink-0" />
                    <span className="flex min-w-0 flex-col gap-0.5">
                      <span className={n.read ? 'font-semibold' : 'font-extrabold'}>{words(t, n)}</span>
                      {/* shortcut: shown in the device's time zone, not the facility's; the facility zone is not in the session yet. */}
                      <span className="text-xs text-muted-foreground">
                        {formatDate(new Date(n.createdAt))}{' · '}{formatTime(new Date(n.createdAt))}{' · '}{n.read ? t('notifications.read') : t('notifications.unread')}
                      </span>
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
