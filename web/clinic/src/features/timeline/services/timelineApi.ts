import { send } from '../../api/services/apiClient';
import { parseFilters, timelineQuery, type TimelineFilters, type TimelinePage, type TrendsAnswer } from '../domain/timeline';

// Timeline (MDV-P2-005). The patient id is the only identifier in a path; filters and the cursor are codes, never patient data.
export const fetchTimeline = (patientId: string, filters: TimelineFilters, before: string | null) =>
  send<TimelinePage>(`/v1/patients/${patientId}/timeline${timelineQuery(filters, before)}`);
export const fetchTrends = (patientId: string) => send<TrendsAnswer>(`/v1/patients/${patientId}/trends`);

// Per user, like the chart section: on a shared computer the next person does not inherit the last one's filters.
const key = (userId: string) => `medvya.timeline.filters.${userId}`;

/** The doctor's filters stay while they move around in this browser session (a tab); only filter codes are stored, never patient data. */
export function rememberedFilters(userId: string): TimelineFilters {
  try {
    return parseFilters(sessionStorage.getItem(key(userId)));
  } catch {
    return parseFilters(null); // storage blocked: start unfiltered
  }
}

export function rememberFilters(userId: string, filters: TimelineFilters) {
  try {
    sessionStorage.setItem(key(userId), JSON.stringify(filters));
  } catch {
    // shortcut: with storage blocked the filters last only until the page is left; nothing is lost but the setting.
  }
}
