import { useState } from 'react';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { ApiError } from '../../schedule/services/scheduleApi';
import { refusalOf } from '../../patient360/domain/chart';
import { toggleKind, trendSeries, type TimelineFilters, type TimelineKind, type TrendChoice } from '../domain/timeline';
import { fetchTimeline, fetchTrends, rememberedFilters, rememberFilters } from '../services/timelineApi';

/**
 * One patient's timeline and trends (MDV-P2-005). Filters combine and are remembered for the browser session; "load earlier" adds older
 * entries below the ones on screen, so the reading position never moves. Every open is audited by the server, so nothing refetches on focus.
 */
export function useTimelineController(patientId: string) {
  const [filters, setFilters] = useState<TimelineFilters>(rememberedFilters);
  const [choice, setChoice] = useState<TrendChoice>('HbA1c');
  const change = (next: TimelineFilters) => { rememberFilters(next); setFilters(next); };

  const list = useInfiniteQuery({
    queryKey: ['timeline', patientId, filters],
    queryFn: ({ pageParam }) => fetchTimeline(patientId, filters, pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: last => last.next,
    retry: false, refetchOnWindowFocus: false,
  });
  const trends = useQuery({ queryKey: ['trends', patientId], queryFn: () => fetchTrends(patientId), retry: false, refetchOnWindowFocus: false });

  const error = list.error instanceof ApiError ? list.error : null;
  const pages = list.data?.pages ?? [];
  return {
    filters,
    entries: pages.flatMap(p => p.entries),
    timeZone: pages[0]?.timeZone ?? trends.data?.timeZone ?? 'UTC',
    loading: list.isPending,
    notFound: error?.status === 404,
    refusal: error?.status === 403 ? (refusalOf(error.body) ?? { canRequest: false, requestPending: false }) : null,
    failed: list.isError && error?.status !== 403 && error?.status !== 404,
    canLoadMore: list.hasNextPage,
    loadingMore: list.isFetchingNextPage,
    loadMoreFailed: list.isFetchNextPageError,
    choice,
    series: trendSeries(trends.data?.trends ?? [], choice),
    trendsLoading: trends.isPending,
    trendsFailed: trends.isError,
    onKind: (kind: TimelineKind) => change(toggleKind(filters, kind)),
    onAllKinds: () => change({ ...filters, kinds: [] }),
    onSource: (source: TimelineFilters['source']) => change({ ...filters, source }),
    onClear: () => change({ kinds: [], source: null }),
    onChoice: setChoice,
    onMore: () => void list.fetchNextPage(),
  };
}
