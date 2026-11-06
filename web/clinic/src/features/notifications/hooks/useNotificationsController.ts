import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { fetchInbox, markAllRead, markRead } from '../services/notificationsApi';

/** Refreshed each minute; there is no push channel yet, so the bell is never more than a minute behind. */
const refreshMs = 60_000;

/** The bell and its overlay for the active facility. Read state lives on the server, so it follows the person across devices. */
export function useNotificationsController(facilityId: string, online: boolean) {
  const queryClient = useQueryClient();
  const key = ['notifications', facilityId];
  const [open, setOpen] = useState(false);
  const query = useQuery({ queryKey: key, queryFn: () => fetchInbox(facilityId), refetchInterval: refreshMs, enabled: online, retry: false });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: key });
  const all = useMutation({ mutationFn: () => markAllRead(facilityId), onSuccess: refresh });
  const one = useMutation({ mutationFn: (id: string) => markRead(facilityId, id), onSuccess: refresh });
  return {
    open,
    onToggle: () => setOpen(o => !o),
    onClose: () => setOpen(false),
    list: query.data ?? null,
    unread: query.data?.unread ?? 0,
    loading: query.isPending && online,
    failed: query.isError,
    online,
    marking: all.isPending || one.isPending,
    onRetry: () => void query.refetch(),
    onMarkAllRead: () => all.mutate(),
    onMarkRead: (id: string) => one.mutate(id),
  };
}

export type NotificationsViewModel = ReturnType<typeof useNotificationsController>;
