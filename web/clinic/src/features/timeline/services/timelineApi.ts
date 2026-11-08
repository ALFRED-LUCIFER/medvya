import { send } from '../../schedule/services/scheduleApi';
import { parseFilters, timelineQuery, type TimelineFilters, type TimelinePage, type TrendsAnswer } from '../domain/timeline';

// Timeline (MDV-P2-005). The patient id is the only identifier in a path; filters and the cursor are codes, never patient data.
export const fetchTimeline = (patientId: string, filters: TimelineFilters, before: string | null) =>
  send<TimelinePage>(`/v1/patients/${patientId}/timeline${timelineQuery(filters, before)}`);
export const fetchTrends = (patientId: string) => send<TrendsAnswer>(`/v1/patients/${patientId}/trends`);

const key = 'medvya.timeline.filters';

/** The doctor's filters stay while they move around in this browser session (a tab); only filter codes are stored, never patient data. */
export function rememberedFilters(): TimelineFilters {
  try {
    return parseFilters(sessionStorage.getItem(key));
  } catch {
    return parseFilters(null); // storage blocked: start unfiltered
  }
}

export function rememberFilters(filters: TimelineFilters) {
  try {
    sessionStorage.setItem(key, JSON.stringify(filters));
  } catch {
    // shortcut: with storage blocked the filters last only until the page is left; nothing is lost but the setting.
  }
}
