import { useEffect, useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { LiveStatus } from '../../front-desk/domain/board';
import { ApiError } from '../../schedule/services/scheduleApi';
import { newlyCalled, type QueueTv, type Unpaired } from '../domain/queueTv';
import { chimePreference, fetchQueueTv, pairDisplay, playChime, takePairingToken, watchQueue } from '../services/queueTvApi';

const key = ['queue-tv'];
const unauthorized = (e: unknown) => e instanceof ApiError && e.status === 401;

/**
 * P-05 waiting-room TV (MDV-P1-031). Opened from its link, the screen pairs once (the token leaves the address at once) and
 * then reads its facility's queue with its display cookie. Changes arrive as empty pushes that trigger a refetch; while not live
 * it refreshes every 30 s. A newly called token chimes when the chime is switched on at the screen.
 */
export function useQueueTvController(online: boolean) {
  const queryClient = useQueryClient();
  const [token] = useState(takePairingToken);
  const pairing = useQuery({ queryKey: ['queue-tv-pair', token], queryFn: () => pairDisplay(token!), enabled: !!token, retry: false, staleTime: Infinity });
  const paired = !token || pairing.isSuccess;

  const [connection, setLive] = useState<LiveStatus>('connecting');
  const live: LiveStatus = online ? connection : 'paused';
  const query = useQuery({
    queryKey: key,
    queryFn: fetchQueueTv,
    enabled: paired,
    refetchInterval: live === 'live' ? false : 30_000,
    retry: (count, error) => !unauthorized(error) && count < 2,
  });
  const unpaired: Unpaired | null = token && pairing.isError ? 'invalid' : unauthorized(query.error) ? (token ? 'invalid' : 'notSetUp') : null;

  useEffect(() => {
    if (!paired || !online || unpaired) return undefined;
    const refresh = () => void queryClient.invalidateQueries({ queryKey: key });
    return watchQueue({ onChange: refresh, onResync: refresh, onStatus: setLive });
  }, [paired, online, unpaired, queryClient]);

  const [chime, setChime] = useState(chimePreference.read);
  const shown = useRef<QueueTv | undefined>(undefined);
  useEffect(() => {
    if (!query.data) return;
    if (chime && newlyCalled(shown.current, query.data).length > 0) playChime();
    shown.current = query.data;
  }, [query.data, chime]);

  return {
    tv: query.data ?? null,
    pairing: !!token && pairing.isPending,
    loading: paired && query.isPending && !unpaired,
    unpaired,
    failed: query.isError && !unpaired && !query.data,
    live,
    chime,
    onToggleChime: () => {
      chimePreference.save(!chime);
      setChime(!chime);
      if (!chime) playChime(); // the press that switches it on lets the browser play sound, and lets staff hear it
    },
  };
}

export type QueueTvViewModel = ReturnType<typeof useQueueTvController>;
