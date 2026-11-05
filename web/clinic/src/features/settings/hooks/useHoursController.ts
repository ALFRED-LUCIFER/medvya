import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { hoursProblems, sessionProblem, templateBody, toDayHours, toHoursRows, toSessionRows, type HoursRow, type SessionRow, type Weekday } from '../domain/settings';
import { fetchHours, fetchTemplates, saveHours, saveTemplate } from '../services/settingsApi';

/**
 * Opening hours of a location: one row per weekday with an open toggle. They inform; bookable time comes from the schedules.
 * An edit belongs to the data it started from (`base`), so changing location or refetching drops a stale edit without an effect.
 */
export function useOpeningHoursController(locationId: string) {
  const queryClient = useQueryClient();
  const key = ['settings', 'hours', locationId];
  const hours = useQuery({ queryKey: key, queryFn: () => fetchHours(locationId), enabled: !!locationId });
  const [edit, setEdit] = useState<{ base: unknown; rows: HoursRow[] } | null>(null);
  const edited = edit && edit.base === hours.data ? edit.rows : null;
  const rows = edited ?? toHoursRows(hours.data ?? []);
  const change = (weekday: Weekday, patch: Partial<HoursRow>) => setEdit({ base: hours.data, rows: rows.map(r => (r.weekday === weekday ? { ...r, ...patch } : r)) });
  const save = useMutation({
    mutationFn: () => saveHours(locationId, toDayHours(rows) ?? []),
    onSuccess: data => { setEdit(null); queryClient.setQueryData(key, data); },
  });
  const problems = hoursProblems(rows);

  return {
    rows,
    problems,
    loading: hours.isPending && !!locationId,
    loadFailed: hours.isError,
    dirty: edited !== null,
    saving: save.isPending,
    saved: save.isSuccess && edited === null,
    saveFailed: save.isError,
    onToggle: (weekday: Weekday, open: boolean) => change(weekday, { open }),
    onTime: (weekday: Weekday, field: 'opens' | 'closes', value: string) => change(weekday, { [field]: value }),
    onSave: () => { if (problems.length === 0) save.mutate(); },
  };
}

/**
 * Bookable schedules of a location (the schedule-template screen left over from MDV-P1-011): pick a doctor or room, edit its
 * slot length and weekly sessions. Breaks, rooms and visit lengths are sent back as saved. Visits the new hours leave outside
 * are listed after saving; the server never moves them.
 */
export function useSchedulesController(locationId: string) {
  const queryClient = useQueryClient();
  const key = ['settings', 'templates', locationId];
  const calendars = useQuery({ queryKey: key, queryFn: () => fetchTemplates(locationId), enabled: !!locationId });
  const [pickedId, setPickedId] = useState<string | null>(null);
  const list = calendars.data ?? [];
  const current = list.find(c => c.calendarId === pickedId) ?? list[0] ?? null;

  const [edit, setEdit] = useState<{ base: unknown; calendarId: string; slot: number | null; sessions: SessionRow[] | null } | null>(null);
  const active = edit && edit.base === calendars.data && edit.calendarId === current?.calendarId ? edit : null;
  const slotMinutes = active?.slot ?? current?.slotMinutes ?? 20;
  const rows = active?.sessions ?? toSessionRows(current?.hours ?? []);
  const problem = sessionProblem(rows);
  const change = (patch: { slot?: number; sessions?: SessionRow[] }) =>
    setEdit({ base: calendars.data, calendarId: current?.calendarId ?? '', slot: active?.slot ?? null, sessions: active?.sessions ?? null, ...patch });

  const save = useMutation({
    mutationFn: () => saveTemplate(templateBody(locationId, current!, slotMinutes, rows)),
    onSuccess: saved => {
      setEdit(null);
      queryClient.setQueryData(key, list.map(c => (c.calendarId === saved.calendarId ? saved : c)));
    },
  });

  const saved = save.isSuccess && active === null && save.data.calendarId === current?.calendarId;

  return {
    calendars: list,
    calendarId: current?.calendarId ?? '',
    loading: calendars.isPending && !!locationId,
    loadFailed: calendars.isError,
    slotMinutes,
    sessions: rows,
    problem,
    dirty: active !== null,
    saving: save.isPending,
    saveFailed: save.isError,
    /** The shown calendar was just saved and not edited since. */
    saved,
    /** Visits left outside the hours just saved, until the next change. */
    outside: saved ? save.data.appointmentsOutsideHours : [],
    onCalendar: setPickedId,
    onSlot: (minutes: number) => change({ slot: minutes }),
    onSession: (index: number, patch: Partial<SessionRow>) => change({ sessions: rows.map((r, i) => (i === index ? { ...r, ...patch } : r)) }),
    onAddSession: () => change({ sessions: [...rows, { weekday: 'Monday', start: '09:00', end: '13:00', room: null }] }),
    onRemoveSession: (index: number) => change({ sessions: rows.filter((_, i) => i !== index) }),
    onSave: () => { if (current && !problem) save.mutate(); },
  };
}
